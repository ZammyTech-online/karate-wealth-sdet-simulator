# Tramo 3 · Lecciones L22-L27 sobre la oleada 3 (privacidad, caos, cumplimiento, voz, webhooks firmados y sagas).
# Necesitan el perfil nivel-9:  nordvik up -Profile nivel-9
from lecciones_d import LOGIN

CLIENTE = r"""
# Ayudante: un cliente nuevo de asesor.erik. Devuelve customerId, nationalId y el login de erik.
# Uso:  * def c = call read('_cliente.feature')
@ignore
Feature: Ayudante · cliente nuevo

  Scenario: Cliente nuevo
    * def erik = call read('this:_login.feature') usuarios.erik
    Given url personUrl
    And path '/api/v1/persons'
    And request { minAge: 25, maxAge: 60 }
    When method post
    Then status 200
    * def p = response.persons[0]
    Given url customerUrl
    And path '/api/v1/customers'
    And headers erik.auth
    And request { firstName: '#(p.firstName)', lastName: '#(p.lastName)', nationalId: '#(p.nationalId)', email: '#(p.email)' }
    When method post
    Then status 201
    * def customerId = response.id
    * def nationalId = p.nationalId
"""

GRABACION = r"""
# Ayudante: un cliente nuevo que YA ha consentido la grabación de llamadas (VOICE_RECORDING).
# Uso:  * def c = call read('_cliente-con-grabacion.feature')
@ignore
Feature: Ayudante · cliente con consentimiento de grabación

  Scenario: Cliente con grabación
    * def c = call read('this:_cliente.feature')
    * def erik = c.erik
    * def customerId = c.customerId
    * def nationalId = c.nationalId
    Given url consentsUrl
    And path '/api/v1/consents'
    And headers erik.auth
    And request { customerId: '#(customerId)', purpose: 'VOICE_RECORDING', granted: true }
    When method post
    Then assert responseStatus == 200 || responseStatus == 201
"""

HMAC_JS = r"""
// Ayudante: HMAC-SHA256 en hexadecimal (minúsculas), como lo calculan el clearing y Nordvik.
// Uso:  * def hmac = read('_hmac.js')
//       * def firma = hmac('secreto', texto)
function(secret, text) {
  var Mac = Java.type('javax.crypto.Mac');
  var Spec = Java.type('javax.crypto.spec.SecretKeySpec');
  var JString = Java.type('java.lang.String');
  var mac = Mac.getInstance('HmacSHA256');
  mac.init(new Spec(new JString(secret).getBytes('UTF-8'), 'HmacSHA256'));
  var bytes = mac.doFinal(new JString(text).getBytes('UTF-8'));
  var hex = '';
  for (var i = 0; i < bytes.length; i++) {
    var h = (bytes[i] & 0xff).toString(16);
    hex += (h.length == 1 ? '0' : '') + h;
  }
  return hex;
}
"""

ORG_JS = r"""
// Ayudante: un número de organización sueco válido y distinto cada vez (NNNNNN-NNNN, tercer dígito >= 2, Luhn).
// Uso:  * def org = read('_org.js')
//       * def numero = org()
function() {
  var d = [5];
  for (var i = 1; i < 9; i++) d.push(Math.floor(Math.random() * 10));
  d[2] = 2 + Math.floor(Math.random() * 8);
  var sum = 0;
  for (var i = 0; i < 9; i++) { var x = d[i] * (i % 2 == 0 ? 2 : 1); sum += x > 9 ? x - 9 : x; }
  d.push((10 - sum % 10) % 10);
  var s = d.join('');
  return s.substring(0, 6) + '-' + s.substring(6);
}
"""

PLAN = r"""
# Ayudante: congela el reloj en el martes de pruebas, crea un cliente nuevo con un plan de pensión PRIVATE
# y una póliza en la gestora simulada (E10). Devuelve customerId, planId, poliza y los logins.
# Uso:  * def t = call read('_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '120000.00', dias: 2, desviacion: 0.5 }
@ignore
Feature: Ayudante · cliente con plan y póliza externa

  Scenario: Preparar un traspaso
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-20T10:00:00' }
    When method post
    Then status 200
    * def c = call read('this:_cliente.feature')
    * def erik = c.erik
    * def olof = call read('this:_login.feature') usuarios.olof
    * def customerId = c.customerId
    Given url pensionPlansUrl
    And path '/api/v1/pension-plans'
    And headers erik.auth
    And request { customerId: '#(customerId)', type: 'PRIVATE', allocation: [{ isin: 'SE0000000203', percent: 100 }] }
    When method post
    Then status 201
    * def planId = response.id
    * def poliza = 'P' + (100000 + Math.floor(Math.random() * 899999))
    Given url providerSimUrl
    And path '/__admin/policies'
    And request { providerCode: '#(gestora)', policyNumber: '#(poliza)', value: '#(valor)', delayDays: '#(dias)', driftPct: '#(desviacion)' }
    When method post
    Then status 200
"""

# ───────────────────────────── L22 ─────────────────────────────
L22 = {
"code": "L22", "slug": "privacidad-tokens-y-pasaportes", "url": "piiVaultUrl",
"readme": r"""
# L22 · Privacidad: tokens, máscaras y pasaportes de un solo uso

**Tramo 3.** Desde aquí trabajas con la oleada 3 entera. Arranca el perfil grande:

```
nordvik up -Profile nivel-9
```

## La idea

En Nordvik un dato personal (número nacional, IBAN, correo, teléfono) **casi nunca viaja en claro**. Se cambia por un
**token** en `pii-vault` (6500) y lo que circula es el token y una **máscara**:

```gherkin
Given url piiVaultUrl
And path '/api/v1/tokens'
And headers karin.auth
And request { type: 'NATIONAL_ID', value: '#(nid)', customerId: '#(ids.ana)' }
When method post
Then status 201
And match response.token == '#regex tok_nid_.+'
And match response.masked contains '**-****'
```

- El mismo valor del mismo cliente da **siempre el mismo token** (la segunda vez, 200 en vez de 201).
- Volver al dato en claro (`/detokenize`) exige **rol y finalidad** (`purpose`): Cumplimiento con `KYC_REVIEW`,
  el propio cliente con `CUSTOMER_REQUEST`. Un asesor no puede (403 `DETOKENIZE_NOT_ALLOWED`).
- Si otro servicio necesita el dato una vez, se le da un **pasaporte** (`data-passport`, 6503): sirve solo para un
  destinatario (`X-Caller-Service`), un número de lecturas y un tiempo. Gastado: 410 `PASSPORT_EXHAUSTED`.

## La norma

1. Comprueba **lo que NO sale**: que el valor en claro no aparece en la respuesta (`match texto !contains nid`).
2. Prueba cada finalidad prohibida, no solo la buena.
3. Un 410 *Gone* significa "existió y ya no": distinto de un 404.

## El fundamento

Privacidad por diseño no se demuestra con el caso feliz: se demuestra buscando el dato donde **no** debería estar.

## Tus ejercicios

`academia corregir L22`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L22. Ejecútalo con:  academia ejemplo L22
@ejemplo
Feature: L22 · Tokens y pasaportes

  Background:
    * def karin = callonce read('ejercicios/_login.feature') usuarios.karin
    * def olof = callonce read('ejercicios/_login.feature') usuarios.olof

  Scenario: Tokenizar un IBAN y verlo en claro con una finalidad válida
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def iban = response.electronic
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'IBAN', value: '#(iban)', customerId: '#(ids.ana)' }
    When method post
    Then status 201
    * def token = response.token
    * match karate.toString(response) !contains iban
    Given path '/api/v1/tokens', token, 'detokenize'
    And headers karin.auth
    And request { purpose: 'KYC_REVIEW' }
    When method post
    Then status 200
    And match response.value == iban

  Scenario: Un pasaporte de un solo uso
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'EMAIL', value: '#("ej" + java.util.UUID.randomUUID().toString().substring(0, 6) + "@example.com")', customerId: '#(ids.ana)' }
    When method post
    Then status 201
    * def token = response.token
    Given url passportsUrl
    And path '/api/v1/passports'
    And headers olof.auth
    And request { token: '#(token)', purpose: 'PAYMENT_EXECUTION', audience: 'payments-gateway' }
    When method post
    Then status 201
    And match response.readsLeft == 1
    * def id = response.id
    Given path '/api/v1/passports', id, 'read'
    And headers olof.auth
    And header X-Caller-Service = 'payments-gateway'
    When method post
    Then status 200
    And match response.readsLeft == 0
""",
"apoyo": {"_login.feature": LOGIN, "_cliente.feature": CLIENTE},
"ejercicios": [
{"id": "E01", "title": "Cambia un dato por un token",
 "enunciado": "Tokeniza el número nacional de un cliente nuevo. Completa el estado y el prefijo del token.",
 "pista1": "Es un recurso nuevo. Los tokens de número nacional empiezan por tok_nid_.",
 "pista2": "201 y '#regex tok_nid_.+'",
 "feature": r"""
  Scenario: Tokenizar
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status ⟦201⟧
    And match response.token == ⟦'#regex tok_nid_.+'⟧
"""},
{"id": "E02", "title": "Lo que NO tiene que salir",
 "enunciado": "La respuesta trae la máscara y nunca el número completo. Completa la máscara y el operador que lo comprueba.",
 "pista1": "La máscara oculta los últimos dígitos así: 19850312-****. Para 'no contiene' Karate usa la negación de contains.",
 "pista2": "'**-****' y !contains",
 "feature": r"""
  Scenario: Máscara y nada en claro
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    And match response.masked contains ⟦'**-****'⟧
    * def texto = karate.toString(response)
    * match texto ⟦!contains⟧ c.nationalId
"""},
{"id": "E03", "title": "El mismo dato, el mismo token",
 "enunciado": "Tokenizar dos veces lo mismo no crea nada nuevo. Completa el estado de la segunda vez y la comparación.",
 "pista1": "No se crea un recurso nuevo: es un 'aquí tienes el que ya existía'.",
 "pista2": "200 y primero (== primero)",
 "feature": r"""
  Scenario: Determinista
    * def c = call read('_cliente.feature')
    * def cuerpo = { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request cuerpo
    When method post
    Then status 201
    * def primero = response.token
    Given path '/api/v1/tokens'
    And headers c.erik.auth
    And request cuerpo
    When method post
    Then status ⟦200⟧
    And match response.token == ⟦primero⟧
"""},
{"id": "E04", "title": "Sin finalidad no hay dato",
 "enunciado": "Karin (Cumplimiento) pide ver el dato en claro sin decir para qué. Completa estado y código.",
 "pista1": "Es un error del que pide: falta un campo obligatorio. El código habla de purpose.",
 "pista2": "400 y 'PURPOSE_REQUIRED'",
 "feature": r"""
  Scenario: Falta purpose
    * def karin = call read('_login.feature') usuarios.karin
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'PHONE', value: '+46 70 555 12 34', customerId: '#(ids.ana)' }
    When method post
    Then assert responseStatus == 200 || responseStatus == 201
    * def token = response.token
    Given path '/api/v1/tokens', token, 'detokenize'
    And headers karin.auth
    And request {}
    When method post
    Then status ⟦400⟧
    And match response.code == ⟦'PURPOSE_REQUIRED'⟧
"""},
{"id": "E05", "title": "Un asesor no ve el dato en claro",
 "enunciado": "Erik intenta destokenizar con una finalidad válida. Completa estado y código.",
 "pista1": "Sabemos quién es y la finalidad es válida, pero su rol no puede. El código empieza por DETOKENIZE.",
 "pista2": "403 y 'DETOKENIZE_NOT_ALLOWED'",
 "feature": r"""
  Scenario: Erik no destokeniza
    * def c = call read('_cliente.feature')
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers c.erik.auth
    And request { type: 'NATIONAL_ID', value: '#(c.nationalId)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    Given path '/api/v1/tokens', response.token, 'detokenize'
    And headers c.erik.auth
    And request { purpose: 'KYC_REVIEW' }
    When method post
    Then status ⟦403⟧
    And match response.code == ⟦'DETOKENIZE_NOT_ALLOWED'⟧
"""},
{"id": "E06", "title": "Un pasaporte se gasta",
 "enunciado": "Olof emite un pasaporte de una lectura para payments-gateway. Completa la cabecera del destinatario y el estado de la segunda lectura.",
 "pista1": "El destinatario se identifica con una cabecera X-...-Service. Un pasaporte gastado ya no existe: no es 404.",
 "pista2": "X-Caller-Service y 410",
 "feature": r"""
  Scenario: Lectura única
    * def olof = call read('_login.feature') usuarios.olof
    * def karin = call read('_login.feature') usuarios.karin
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'EMAIL', value: '#("e6" + java.util.UUID.randomUUID().toString().substring(0, 6) + "@example.com")', customerId: '#(ids.ana)' }
    When method post
    Then status 201
    * def token = response.token
    Given url passportsUrl
    And path '/api/v1/passports'
    And headers olof.auth
    And request { token: '#(token)', purpose: 'PAYMENT_EXECUTION', audience: 'payments-gateway' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/passports', id, 'read'
    And headers olof.auth
    And header ⟦X-Caller-Service⟧ = 'payments-gateway'
    When method post
    Then status 200
    Given path '/api/v1/passports', id, 'read'
    And headers olof.auth
    And header X-Caller-Service = 'payments-gateway'
    When method post
    Then status ⟦410⟧
    And match response.code == 'PASSPORT_EXHAUSTED'
"""},
],
}

# ───────────────────────────── L23 ─────────────────────────────
L23 = {
"code": "L23", "slug": "caos-y-resiliencia", "url": "chaosUrl",
"readme": r"""
# L23 · Caos y resiliencia: provocar fallos a propósito

## La idea

Un sistema real falla: latencias, 503, simuladores caídos. Nordvik te deja **provocar** esos fallos para probar que
tu cliente (y tus tests) reaccionan bien.

**1 · Para una sola petición**, la cabecera `X-Chaos-Scenario`:

```gherkin
Given url uuidUrl
And path '/health'
And header X-Chaos-Scenario = 'status=503'
When method get
Then status 503
And match response.code == 'CHAOS_INJECTED'
```

Valores: `status=500` (cualquier 4xx/5xx), `latency=300` (milisegundos), o los dos: `latency=200;status=503`.

**2 · Para un servicio**, una regla en `chaos-control` (7600) que dura N veces:

```gherkin
Given url chaosUrl
And path '/api/v1/faults'
And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 2 }
When method post
Then status 201
```

**3 · Un simulador entero**, desde `mock-control` (7601): `PUT /api/v1/mocks/{nombre}/behaviour { mode: 'down' }`.

Karate te da `responseTime` (ms) para medir latencias y `retry until` para reintentar hasta que algo se cumpla.

## La norma

1. **Deja todo como estaba**: `POST /api/v1/reset` en chaos-control y `POST /api/v1/mocks/reset` en mock-control.
   En esta lección el `Background` lo hace por ti.
2. Un fallo inyectado lleva la cabecera `X-Chaos-Injected`: así distingues tu caos de un fallo real.
3. Reintentar está bien para fallos **transitorios** (503); nunca reintentes un 400.

## El fundamento

La resiliencia no se supone: se prueba rompiendo cosas en un entorno donde romper es gratis.
""",
"ejemplo": r"""
# Ejemplo resuelto de L23. Ejecútalo con:  academia ejemplo L23
@ejemplo
Feature: L23 · Caos

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Un 503 solo para esta petición
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'status=503'
    When method get
    Then status 503
    And match response.code == 'CHAOS_INJECTED'

  Scenario: Una regla de dos fallos y un reintento que la supera
    Given url chaosUrl
    And path '/api/v1/faults'
    And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 2 }
    When method post
    Then status 201
    * configure retry = { count: 5, interval: 100 }
    Given url uuidUrl
    And path '/api/v1/uuids'
    And retry until responseStatus == 200
    When method get
    Then status 200
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Un fallo de una sola petición",
 "enunciado": "Pide /health de uuid-service con un 500 inyectado. Completa la cabecera y el código del problema.",
 "pista1": "La cabecera es X-Chaos-... (mira el README). El código dice que el fallo es inyectado.",
 "pista2": "X-Chaos-Scenario y 'CHAOS_INJECTED'",
 "feature": r"""
  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: 500 a propósito
    Given url uuidUrl
    And path '/health'
    And header ⟦X-Chaos-Scenario⟧ = 'status=500'
    When method get
    Then status 500
    And match response.code == ⟦'CHAOS_INJECTED'⟧
"""},
{"id": "E02", "title": "Mide la latencia",
 "enunciado": "Inyecta 300 ms de latencia y comprueba que la respuesta tardó al menos 280 ms. Completa el valor de la cabecera y la variable de Karate con el tiempo.",
 "pista1": "El escenario se escribe clave=valor. Karate guarda el tiempo de la última respuesta en una variable response...",
 "pista2": "'latency=300' y responseTime",
 "feature": r"""
  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Lento pero bien
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = ⟦'latency=300'⟧
    When method get
    Then status 200
    And assert ⟦responseTime⟧ >= 280
"""},
{"id": "E03", "title": "Un escenario mal escrito",
 "enunciado": "status=200 no es un fallo: Nordvik lo rechaza. Completa estado y código.",
 "pista1": "Es un error del que pide. El código habla de un escenario de caos no válido.",
 "pista2": "400 y 'INVALID_CHAOS_SCENARIO'",
 "feature": r"""
  Scenario: Caos inválido
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'status=200'
    When method get
    Then status ⟦400⟧
    And match response.code == ⟦'INVALID_CHAOS_SCENARIO'⟧
"""},
{"id": "E04", "title": "Reintenta hasta que pase",
 "enunciado": "Una regla tumba /api/v1/uuids dos veces. Completa la condición de reintento para llegar al 200.",
 "pista1": "retry until evalúa una expresión con el estado de la respuesta (no response.status: aquí el cuerpo no tiene status).",
 "pista2": "responseStatus == 200",
 "feature": r"""
  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Dos fallos y a la tercera
    Given url chaosUrl
    And path '/api/v1/faults'
    And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 2 }
    When method post
    Then status 201
    * configure retry = { count: 5, interval: 100 }
    Given url uuidUrl
    And path '/api/v1/uuids'
    And retry until ⟦responseStatus == 200⟧
    When method get
    Then status 200
"""},
{"id": "E05", "title": "¿Tu caos o un fallo real?",
 "enunciado": "El fallo inyectado por una regla lleva su id en la cabecera X-Chaos-Injected. Completa el nombre de la cabecera y con qué se compara.",
 "pista1": "karate.response.header('...') no distingue mayúsculas. El id es el de la regla que creaste.",
 "pista2": "'X-Chaos-Injected' y regla",
 "feature": r"""
  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Rastrear el fallo
    Given url chaosUrl
    And path '/api/v1/faults'
    And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 1 }
    When method post
    Then status 201
    * def regla = response.id
    Given url uuidUrl
    And path '/api/v1/uuids'
    When method get
    Then status 503
    And match karate.response.header(⟦'X-Chaos-Injected'⟧) == ⟦regla⟧
"""},
{"id": "E06", "title": "Un simulador caído",
 "enunciado": "Tumba el simulador de listas de sanciones y comprueba que Nordvik lo dice claro. Completa el modo y el código.",
 "pista1": "Los modos son normal, down, error y slow. El código dice que el proveedor de screening está caído.",
 "pista2": "'down' y 'SCREENING_PROVIDER_DOWN'",
 "feature": r"""
  Background:
    Given url mockControlUrl
    And path '/api/v1/mocks/reset'
    When method post
    Then status 200

  Scenario: Proveedor caído
    * def karin = call read('_login.feature') usuarios.karin
    Given url mockControlUrl
    And path '/api/v1/mocks/sanctionscreen-sim/behaviour'
    And request { mode: ⟦'down'⟧ }
    When method put
    Then status 200
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status 503
    And match response.code == ⟦'SCREENING_PROVIDER_DOWN'⟧
    Given url mockControlUrl
    And path '/api/v1/mocks/reset'
    When method post
    Then status 200
"""},
],
}

# ───────────────────────────── L24 ─────────────────────────────
L24 = {
"code": "L24", "slug": "kyc-y-sanciones", "url": "sanctionsUrl",
"readme": r"""
# L24 · Cumplimiento: listas de sanciones y coincidencia difusa

## La idea

Antes de aceptar dinero, Nordvik comprueba que el cliente no esté en listas de sanciones. `sanctions-adapter` (6401)
llama al proveedor externo **SanctionScreen** (simulador E3, 7703) y devuelve:

| status | Qué significa |
|---|---|
| `CLEAR` | Nadie en las listas se parece lo bastante |
| `POTENTIAL_MATCH` | Alguien se parece: una persona de Cumplimiento tiene que decidir |
| `CONFIRMED_MATCH` / `CLEARED` | Ya decidido (`TRUE_MATCH` / `FALSE_POSITIVE`) |
| `INCOMPLETE` | El proveedor no miró todas las listas: **nunca** cuenta como limpio |

La coincidencia es **difusa**: "Viktor Draganow" se parece a "Viktor Draganov" (puntuación 0-100). El umbral es 85
y se cambia en `config-service` (`sanctions-adapter/matchThreshold`).

```gherkin
Given url sanctionsUrl
And path '/api/v1/screenings'
And headers karin.auth
And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
When method post
Then status 201
And match response.status == 'POTENTIAL_MATCH'
And match response.matches[0] contains { entryId: 'EU-0001', score: '#? _ >= 85' }
```

## La norma

1. Prueba los **casi**: errores de tecleo, nombre y apellido al revés, acentos.
2. Una decisión es de Cumplimiento (Karin), nunca del asesor. Y se toma **una vez** (luego 409).
3. Un resultado parcial jamás es CLEAR: búscalo a propósito con `/__admin/partial?on=true` en el simulador.

## El fundamento

En cumplimiento normativo, un falso negativo (dejar pasar a alguien sancionado) es mucho más caro que un falso positivo.
Por eso los tests de esta lección buscan los grises.
""",
"ejemplo": r"""
# Ejemplo resuelto de L24. Ejecútalo con:  academia ejemplo L24
@ejemplo
Feature: L24 · Sanciones

  Background:
    * def karin = callonce read('ejercicios/_login.feature') usuarios.karin

  Scenario: Un nombre limpio
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status 201
    And match response contains { status: 'CLEAR', threshold: 85, matches: [] }

  Scenario: Coincidencia exacta y decisión de Cumplimiento
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
    When method post
    Then status 201
    And match response.status == 'POTENTIAL_MATCH'
    Given path '/api/v1/screenings', response.id, 'decision'
    And headers karin.auth
    And request { decision: 'TRUE_MATCH', reason: 'Mismo nombre y fecha de nacimiento' }
    When method post
    Then status 200
    And match response.status == 'CONFIRMED_MATCH'
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Limpio",
 "enunciado": "Comprueba a 'Persona Corriente'. Completa el estado HTTP y el status del resultado.",
 "pista1": "Una comprobación nueva se crea. Nadie se parece a esa persona.",
 "pista2": "201 y 'CLEAR'",
 "feature": r"""
  Scenario: Persona corriente
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status ⟦201⟧
    And match response.status == ⟦'CLEAR'⟧
"""},
{"id": "E02", "title": "Un error de tecleo no te salva",
 "enunciado": "'Viktor Draganow' (con w) también salta. Completa el status esperado.",
 "pista1": "La coincidencia es difusa: se parece mucho, así que alguien tiene que revisarlo.",
 "pista2": "'POTENTIAL_MATCH'",
 "feature": r"""
  Scenario: Casi el mismo nombre
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganow', birthDate: '1961-04-12' }
    When method post
    Then status 201
    And match response.status == ⟦'POTENTIAL_MATCH'⟧
"""},
{"id": "E03", "title": "La puntuación por encima del umbral",
 "enunciado": "Valida la primera coincidencia con un marcador que acepte cualquier puntuación >= 85.",
 "pista1": "Un marcador con condición se escribe '#? ' seguido de una expresión con _ como el valor.",
 "pista2": "'#? _ >= 85'",
 "feature": r"""
  Scenario: Puntuación
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Draganov Viktor' }
    When method post
    Then status 201
    And match response.matches[0] contains { entryId: 'EU-0001', score: ⟦'#? _ >= 85'⟧ }
"""},
{"id": "E04", "title": "El asesor no decide",
 "enunciado": "Erik intenta resolver una coincidencia. Completa estado y código.",
 "pista1": "Erik es quien es, pero decidir es cosa de Cumplimiento: falta el rol.",
 "pista2": "403 y 'ROLE_NOT_ALLOWED'",
 "feature": r"""
  Scenario: Decide Cumplimiento
    * def karin = call read('_login.feature') usuarios.karin
    * def erik = call read('_login.feature') usuarios.erik
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
    When method post
    Then status 201
    Given path '/api/v1/screenings', response.id, 'decision'
    And headers erik.auth
    And request { decision: 'FALSE_POSITIVE', reason: 'No es él' }
    When method post
    Then status ⟦403⟧
    And match response.code == ⟦'ROLE_NOT_ALLOWED'⟧
"""},
{"id": "E05", "title": "Se decide una vez",
 "enunciado": "Karin confirma la coincidencia y luego intenta cambiar de opinión. Completa el status tras decidir y el estado HTTP del segundo intento.",
 "pista1": "TRUE_MATCH confirma la coincidencia. Una vez decidido no queda nada que decidir: conflicto.",
 "pista2": "'CONFIRMED_MATCH' y 409",
 "feature": r"""
  Scenario: Decisión única
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/screenings', id, 'decision'
    And headers karin.auth
    And request { decision: 'TRUE_MATCH', reason: 'Mismo nombre y fecha' }
    When method post
    Then status 200
    And match response.status == ⟦'CONFIRMED_MATCH'⟧
    Given path '/api/v1/screenings', id, 'decision'
    And headers karin.auth
    And request { decision: 'FALSE_POSITIVE', reason: 'Cambio de opinión' }
    When method post
    Then status ⟦409⟧
    And match response.code == 'NOTHING_TO_DECIDE'
"""},
{"id": "E06", "title": "Parcial nunca es limpio",
 "enunciado": "Pon el simulador a responder solo algunas listas. Completa el status del resultado.",
 "pista1": "No es CLEAR aunque nadie coincida: el proveedor no miró todo.",
 "pista2": "'INCOMPLETE'",
 "feature": r"""
  Scenario: Respuesta parcial
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionSimUrl
    And path '/__admin/partial'
    And param on = true
    When method post
    Then status 200
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    * def resultado = response
    Given url sanctionSimUrl
    And path '/__admin/partial'
    And param on = false
    When method post
    Then status 200
    * match resultado.status == ⟦'INCOMPLETE'⟧
    * match resultado.partial == true
"""},
],
}

# ───────────────────────────── L25 ─────────────────────────────
L25 = {
"code": "L25", "slug": "voz-asincrona-y-destruccion", "url": "voiceUrl",
"readme": r"""
# L25 · Llamadas de voz: procesos asíncronos que se autodestruyen

## La idea

Una llamada con un asesor pasa por un **pipeline** que no termina cuando tu petición responde:

```
POST /api/v2/calls  (201)  →  play-script (202)  →  end (202)
   … transcripción → redacción de datos personales → extracción de datos → resumen …
   →  status AWAITING_REVIEW  o  SHREDDED (audio y transcripción destruidos, recibo firmado)
```

Un **202 Accepted** significa "lo he recibido, lo haré": tu test tiene que **esperar** con `retry until`.

```gherkin
* configure retry = { count: 30, interval: 500 }
Given url voiceUrl
And path '/api/v2/calls', id
And headers erik.auth
And retry until response.status == 'SHREDDED'
When method get
Then status 200
```

Reglas de Nordvik:
- Sin el consentimiento `VOICE_RECORDING` no hay llamada (409 `RECORDING_CONSENT_MISSING`).
- Un número nacional dictado en la llamada **nunca** aparece en la transcripción: sale `[NATIONAL_ID]`.
- Al destruir, Nordvik emite un **recibo firmado**: `POST /api/v2/receipts/verify` dice si es auténtico.

El simulador de telefonía (E12) tiene guiones: `v2-consulta-corta` (se resume y se destruye) y
`v3-dicta-numero` (el cliente dicta su número; queda en revisión).

## La norma

1. Tras un 202, **nunca** compruebes el resultado sin esperar.
2. Pon un `count` y un `interval` razonables: que el test falle claro si el proceso se cuelga.
3. Después de destruir, comprueba que lo destruido **ya no se puede pedir** (404).
""",
"ejemplo": r"""
# Ejemplo resuelto de L25. Ejecútalo con:  academia ejemplo L25
@ejemplo
Feature: L25 · Una llamada corta de principio a fin

  Scenario: Llamar, esperar y comprobar el recibo
    * def c = call read('ejercicios/_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v2-consulta-corta' }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until response.status == 'SHREDDED'
    When method get
    Then status 200
    Given path '/api/v2/calls', id, 'destruction-receipt'
    And headers c.erik.auth
    When method get
    Then status 200
    Given path '/api/v2/receipts/verify'
    And request { receipt: '#(response.receipt)', signature: '#(response.signature)' }
    When method post
    Then status 200
    And match response.valid == true
""",
"apoyo": {"_login.feature": LOGIN, "_cliente.feature": CLIENTE, "_cliente-con-grabacion.feature": GRABACION},
"ejercicios": [
{"id": "E01", "title": "Sin consentimiento no hay llamada",
 "enunciado": "Un cliente que no ha consentido la grabación. Completa estado y código.",
 "pista1": "La petición es correcta, pero choca con el estado del cliente: conflicto.",
 "pista2": "409 y 'RECORDING_CONSENT_MISSING'",
 "feature": r"""
  Scenario: Sin consentimiento
    * def c = call read('_cliente.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status ⟦409⟧
    And match response.code == ⟦'RECORDING_CONSENT_MISSING'⟧
"""},
{"id": "E02", "title": "Aceptado no es terminado",
 "enunciado": "Lanza el guion y cuelga. Completa los dos estados HTTP.",
 "pista1": "Nordvik acepta el trabajo y lo hará después.",
 "pista2": "202 y 202",
 "feature": r"""
  Scenario: Guion y colgar
    * def c = call read('_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v2-consulta-corta' }
    When method post
    Then status ⟦202⟧
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status ⟦202⟧
"""},
{"id": "E03", "title": "Espera al final del pipeline",
 "enunciado": "Completa la condición de espera y el número de trozos de audio que quedan guardados al final.",
 "pista1": "La llamada corta termina destruida. Destruida quiere decir que no queda ningún trozo.",
 "pista2": "response.status == 'SHREDDED' y 0",
 "feature": r"""
  Scenario: Hasta la destrucción
    * def c = call read('_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v2-consulta-corta' }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until ⟦response.status == 'SHREDDED'⟧
    When method get
    Then status 200
    And match response.storedChunks == ⟦0⟧
"""},
{"id": "E04", "title": "Lo destruido no vuelve",
 "enunciado": "Tras la destrucción, la transcripción ya no se puede pedir. Completa estado y código.",
 "pista1": "Ya no hay transcripción que devolver. El código dice que no está disponible.",
 "pista2": "404 y 'TRANSCRIPT_NOT_AVAILABLE'",
 "feature": r"""
  Scenario: Transcripción destruida
    * def c = call read('_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v2-consulta-corta' }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until response.status == 'SHREDDED'
    When method get
    Given path '/api/v2/calls', id, 'transcript'
    And headers c.erik.auth
    When method get
    Then status ⟦404⟧
    And match response.code == ⟦'TRANSCRIPT_NOT_AVAILABLE'⟧
"""},
{"id": "E05", "title": "Un recibo que no se puede falsificar",
 "enunciado": "Cambia una letra del recibo: la verificación tiene que decir que no vale. Completa los dos resultados.",
 "pista1": "El recibo original es auténtico; el manipulado no.",
 "pista2": "true y false",
 "feature": r"""
  Scenario: Recibo firmado
    * def c = call read('_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v2-consulta-corta' }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until response.status == 'SHREDDED'
    When method get
    Given path '/api/v2/calls', id, 'destruction-receipt'
    And headers c.erik.auth
    When method get
    Then status 200
    * def recibo = response
    Given path '/api/v2/receipts/verify'
    And request { receipt: '#(recibo.receipt)', signature: '#(recibo.signature)' }
    When method post
    Then match response.valid == ⟦true⟧
    * def falso = recibo.receipt.replace(id, id + 'x')
    Given path '/api/v2/receipts/verify'
    And request { receipt: '#(falso)', signature: '#(recibo.signature)' }
    When method post
    Then match response.valid == ⟦false⟧
"""},
{"id": "E06", "title": "El número dictado no se transcribe",
 "enunciado": "El cliente dicta su número nacional. Completa el estado al que llega la llamada y lo que aparece en su lugar.",
 "pista1": "Esta llamada tiene datos que confirmar: se queda esperando revisión. El número se sustituye por una etiqueta entre corchetes.",
 "pista2": "'AWAITING_REVIEW' y '[NATIONAL_ID]'",
 "feature": r"""
  Scenario: Redacción en la transcripción
    * def c = call read('_cliente-con-grabacion.feature')
    Given url voiceUrl
    And path '/api/v2/calls'
    And headers c.erik.auth
    And request { customerId: '#(c.customerId)', channel: 'PHONE' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/calls', id, 'play-script'
    And headers c.erik.auth
    And request { scriptId: 'v3-dicta-numero', variables: { PNR: '#(c.nationalId)' } }
    When method post
    Then status 202
    Given path '/api/v2/calls', id, 'end'
    And headers c.erik.auth
    When method post
    Then status 202
    * configure retry = { count: 40, interval: 500 }
    Given path '/api/v2/calls', id
    And headers c.erik.auth
    And retry until response.status == ⟦'AWAITING_REVIEW'⟧
    When method get
    Given path '/api/v2/calls', id, 'transcript'
    And headers c.erik.auth
    When method get
    Then status 200
    And match response.turns[1].text contains ⟦'[NATIONAL_ID]'⟧
    And match karate.toString(response) !contains c.nationalId
"""},
],
}

# ───────────────────────────── L26 ─────────────────────────────
L26 = {
"code": "L26", "slug": "webhooks-firmados-y-ficheros", "url": "clearingAdapterUrl",
"readme": r"""
# L26 · Webhooks firmados y ficheros de empresas (pensiones)

## La idea

Cada mes, el **Clearing de Empleadores** (simulador E9, 7709) junta las aportaciones de una empresa en un CSV y avisa a
Nordvik con un **webhook**. ¿Cómo sabe Nordvik que el aviso es auténtico? Por la cabecera
`X-Signature: sha256=<HMAC-SHA256 del cuerpo EXACTO con un secreto compartido>`.

En Karate firmas con Java desde JavaScript (el ayudante `_hmac.js` lo hace por ti):

```gherkin
* def hmac = read('_hmac.js')
* text cuerpo =
  TRIPLE
  {"eventType":"FILE_AVAILABLE","fileId":"cf_x","employerOrgNumber":"556036-0793","period":"2026-10"}
  TRIPLE
Given url clearingAdapterUrl
And path '/api/v1/webhooks/clearing'
And header X-Signature = 'sha256=' + hmac('e9-clearing-webhook-2026', cuerpo)
And request cuerpo
When method post
```

**Ojo:** `text` guarda el cuerpo como texto tal cual. Si usas `def`, Karate lo convierte en JSON y al enviarlo puede
cambiar espacios u orden: la firma dejaría de cuadrar (es la trampa T18).

El flujo completo (F-06): la empresa da de alta a sus empleados en `employer-portal-api` (7102) con su
`X-Employer-Key`; el clearing publica el fichero; Nordvik valida **línea a línea** y devuelve un acuse con
`accepted` y `rejected[]` (cada rechazo con su `code`), sin números nacionales en claro.

## La norma

1. Firma **el texto exacto** que envías. Nada de reconstruir el JSON.
2. Prueba tres firmas: ninguna, una con otro secreto y la buena.
3. En los ficheros, prueba cada código de rechazo con una línea dedicada.
""",
"ejemplo": r"""
# Ejemplo resuelto de L26. Ejecútalo con:  academia ejemplo L26
@ejemplo
Feature: L26 · Firmar un webhook

  Scenario: Firma buena: Nordvik la acepta e intenta descargar el fichero
    * def hmac = read('ejercicios/_hmac.js')
    * text cuerpo =
      TRIPLE
      {"eventType":"FILE_AVAILABLE","fileId":"cf_ejemplo","employerOrgNumber":"556036-0793","period":"2026-10"}
      TRIPLE
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And header X-Signature = 'sha256=' + hmac('e9-clearing-webhook-2026', cuerpo)
    And request cuerpo
    When method post
    # La firma pasa; el fichero cf_ejemplo no existe en el clearing: 502 al descargarlo
    Then status 502
    And match response.code == 'FILE_DOWNLOAD_FAILED'
""",
"apoyo": {"_login.feature": LOGIN, "_cliente.feature": CLIENTE, "_hmac.js": HMAC_JS, "_org.js": ORG_JS},
"ejercicios": [
{"id": "E01", "title": "Sin firma, fuera",
 "enunciado": "Manda el webhook sin X-Signature. Completa estado y código.",
 "pista1": "No sabemos quién lo manda. El código habla de una firma no válida.",
 "pista2": "401 y 'INVALID_SIGNATURE'",
 "feature": r"""
  Scenario: Sin firma
    * text cuerpo =
      TRIPLE
      {"eventType":"FILE_AVAILABLE","fileId":"cf_e01","employerOrgNumber":"556036-0793","period":"2026-10"}
      TRIPLE
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And request cuerpo
    When method post
    Then status ⟦401⟧
    And match response.code == ⟦'INVALID_SIGNATURE'⟧
"""},
{"id": "E02", "title": "Firmado con otro secreto",
 "enunciado": "Una firma bien formada pero con un secreto equivocado tampoco vale. Completa el estado.",
 "pista1": "Para Nordvik es igual que no firmar.",
 "pista2": "401",
 "feature": r"""
  Scenario: Secreto equivocado
    * def hmac = read('_hmac.js')
    * text cuerpo =
      TRIPLE
      {"eventType":"FILE_AVAILABLE","fileId":"cf_e02","employerOrgNumber":"556036-0793","period":"2026-10"}
      TRIPLE
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And header X-Signature = 'sha256=' + hmac('no-es-el-secreto', cuerpo)
    And request cuerpo
    When method post
    Then status ⟦401⟧
"""},
{"id": "E03", "title": "La firma buena",
 "enunciado": "Firma con el secreto del clearing (e9-clearing-webhook-2026). Completa la llamada al ayudante y el código que demuestra que la firma pasó.",
 "pista1": "hmac(secreto, texto). Si la firma pasa, Nordvik intenta descargar un fichero que no existe.",
 "pista2": "hmac('e9-clearing-webhook-2026', cuerpo) y 'FILE_DOWNLOAD_FAILED'",
 "feature": r"""
  Scenario: Firma correcta
    * def hmac = read('_hmac.js')
    * text cuerpo =
      TRIPLE
      {"eventType":"FILE_AVAILABLE","fileId":"cf_e03","employerOrgNumber":"556036-0793","period":"2026-10"}
      TRIPLE
    Given url clearingAdapterUrl
    And path '/api/v1/webhooks/clearing'
    And header Content-Type = 'application/json'
    And header X-Signature = 'sha256=' + ⟦hmac('e9-clearing-webhook-2026', cuerpo)⟧
    And request cuerpo
    When method post
    Then status 502
    And match response.code == ⟦'FILE_DOWNLOAD_FAILED'⟧
"""},
{"id": "E04", "title": "La llave de la empresa",
 "enunciado": "El admin da de alta una empresa y recibe su API key. Completa el nombre de la cabecera con la que la empresa se identifica.",
 "pista1": "Es una cabecera X-...-Key propia de empresas (mira el README).",
 "pista2": "X-Employer-Key",
 "feature": r"""
  Scenario: Empresa con API key
    * def admin = call read('_login.feature') usuarios.admin
    * def org = read('_org.js')
    Given url employerPortalUrl
    And path '/api/v1/employers'
    And headers admin.auth
    And request { orgNumber: '#(org())', name: 'Fjällbygg AB', agreement: 'ITP1' }
    When method post
    Then status 201
    * def llave = response.apiKey
    Given path '/api/v1/employer/employees'
    When method get
    Then status 401
    And match response.code == 'EMPLOYER_KEY_REQUIRED'
    Given path '/api/v1/employer/employees'
    And header ⟦X-Employer-Key⟧ = llave
    When method get
    Then status 200
    And match response.count == 0
"""},
{"id": "E05", "title": "Un fichero, un acuse",
 "enunciado": "Una empresa con un empleado manda un fichero con una línea buena y la misma repetida. Completa cuántas acepta y el código de la repetida.",
 "pista1": "La primera línea entra. La segunda es el mismo empleado y el mismo mes.",
 "pista2": "1 y 'DUPLICATE_MONTH'",
 "feature": r"""
  Scenario: Acuse del fichero
    * def admin = call read('_login.feature') usuarios.admin
    * def org = read('_org.js')
    * def empresa = org()
    Given url employerPortalUrl
    And path '/api/v1/employers'
    And headers admin.auth
    And request { orgNumber: '#(empresa)', name: 'Fjällbygg AB', agreement: 'ITP1' }
    When method post
    Then status 201
    * def llave = response.apiKey
    * def c = call read('_cliente.feature')
    Given path '/api/v1/employer/employees'
    And header X-Employer-Key = llave
    And request { personalNumber: '#(c.nationalId)' }
    When method post
    Then status 201
    Given url clearingSimUrl
    And path '/__admin/files'
    And request { employerOrgNumber: '#(empresa)', period: '2026-10', lines: [ { personalNumber: '#(c.nationalId)', amount: '2500.00', agreement: 'ITP1' }, { personalNumber: '#(c.nationalId)', amount: '2500.00', agreement: 'ITP1' } ] }
    When method post
    Then status 201
    And match response.webhookStatus == 200
    Given path '/__admin/files', response.fileId, 'receipt'
    When method get
    Then status 200
    And match response.accepted == ⟦1⟧
    And match response.rejected[0].code == ⟦'DUPLICATE_MONTH'⟧
"""},
{"id": "E06", "title": "El acuse no lleva números en claro",
 "enunciado": "Una línea de alguien que no es cliente. Completa el código de rechazo y el operador que comprueba que el número no sale.",
 "pista1": "Nordvik no conoce a esa persona. Para 'no contiene', la negación de contains.",
 "pista2": "'UNKNOWN_EMPLOYEE' y !contains",
 "feature": r"""
  Scenario: Desconocido y enmascarado
    * def admin = call read('_login.feature') usuarios.admin
    * def org = read('_org.js')
    * def empresa = org()
    Given url employerPortalUrl
    And path '/api/v1/employers'
    And headers admin.auth
    And request { orgNumber: '#(empresa)', name: 'Fjällbygg AB', agreement: 'ITP1' }
    When method post
    Then status 201
    Given url personUrl
    And path '/api/v1/persons'
    And request { minAge: 30, maxAge: 50 }
    When method post
    Then status 200
    * def extrano = response.persons[0].nationalId
    Given url clearingSimUrl
    And path '/__admin/files'
    And request { employerOrgNumber: '#(empresa)', period: '2026-10', lines: [ { personalNumber: '#(extrano)', amount: '1000.00', agreement: 'ITP1' } ] }
    When method post
    Then status 201
    Given path '/__admin/files', response.fileId, 'receipt'
    When method get
    Then status 200
    And match response.rejected[0].code == ⟦'UNKNOWN_EMPLOYEE'⟧
    * def texto = karate.toString(response)
    * match texto ⟦!contains⟧ extrano
"""},
],
}

# ───────────────────────────── L27 ─────────────────────────────
TRANSFER_BG = r"""
  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }
"""

L27 = {
"code": "L27", "slug": "sagas-y-reloj-virtual", "url": "transfersUrl",
"readme": r"""
# L27 · Sagas largas y reloj virtual: el traspaso de una pensión

## La idea

Traspasar una pensión desde otra gestora tarda **días**: la gestora (simulador E10, 7710) contesta la valoración
en 1-3 días. Un test no puede esperar días… pero Nordvik tiene **reloj virtual** (`clock-service`, 6003):

```gherkin
Given url clockUrl
And path '/api/v1/clock/freeze'
And request { at: '2026-10-22T10:30:00' }
When method post
```

La saga de `transfer-service` (7108) es una **máquina de estados**:

```
VALUATION_PENDING → VALUED → (cliente: /confirm) AWAITING_APPROVAL → (Operaciones: /approve)
   → COMPLETED            si lo recibido está a ±2 % de la valoración
   → MANUAL_REVIEW        si no  → /accept-amount (COMPLETED) o /reject (REJECTED)
```

Cualquier paso fuera de orden es **409 `INVALID_STATE_TRANSITION`**, y cada paso queda en `history`.

El ayudante `_traspaso-listo.feature` congela el reloj en el martes 2026-10-20 a las 10:00, crea un cliente con un plan
de pensión y le inventa una póliza en la gestora con el valor, los días de demora y la desviación que digas.

## La norma

1. **Devuelve el reloj** a la hora real al acabar: aquí lo hace `afterScenario` en el `Background`.
   Y ojo: los tokens caducan también en tiempo virtual. Si adelantas días, vuelve a hacer login.
2. Prueba cada transición prohibida, no solo el camino feliz.
3. Valida el historial completo con `match response.history[*].to == [...]`.

## El fundamento

En una saga el bug típico no está en un paso: está en el **orden** de los pasos. El reloj virtual convierte días
en milisegundos y te deja probar ese orden de forma determinista.
""",
"ejemplo": r"""
# Ejemplo resuelto de L27. Ejecútalo con:  academia ejemplo L27
@ejemplo
Feature: L27 · Un traspaso completo
""" + TRANSFER_BG + r"""
  Scenario: De la solicitud al dinero
    * def t = call read('ejercicios/_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '120000.00', dias: 2, desviacion: 0.5 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'FJORDLIV', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    And match response.status == 'VALUATION_PENDING'
    * def id = response.id
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-22T10:30:00' }
    When method post
    Then status 200
    # Los tokens también caducan en tiempo virtual: tras dos días, login otra vez
    * def erik = call read('ejercicios/_login.feature') usuarios.erik
    * def olof = call read('ejercicios/_login.feature') usuarios.olof
    Given url transfersUrl
    And path '/api/v1/transfers', id
    And headers erik.auth
    When method get
    Then match response.status == 'VALUED'
    Given path '/api/v1/transfers', id, 'confirm'
    And headers erik.auth
    When method post
    Then match response.status == 'AWAITING_APPROVAL'
    Given path '/api/v1/transfers', id, 'approve'
    And headers olof.auth
    When method post
    Then status 200
    And match response contains { status: 'COMPLETED', receivedAmount: '120600.00', deviationPct: '0.50' }
""",
"apoyo": {"_login.feature": LOGIN, "_cliente.feature": CLIENTE, "_traspaso-listo.feature": PLAN},
"ejercicios": [
{"id": "E01", "title": "Empieza esperando",
 "enunciado": "Solicita el traspaso. Completa el estado HTTP y el estado de la saga.",
 "pista1": "Se crea la solicitud, pero la gestora aún no ha valorado la póliza.",
 "pista2": "201 y 'VALUATION_PENDING'",
 "feature": TRANSFER_BG + r"""
  Scenario: Solicitud
    * def t = call read('_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '80000.00', dias: 2, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'FJORDLIV', policyNumber: '#(t.poliza)' }
    When method post
    Then status ⟦201⟧
    And match response.status == ⟦'VALUATION_PENDING'⟧
"""},
{"id": "E02", "title": "No te saltes pasos",
 "enunciado": "Confirma antes de que llegue la valoración. Completa estado y código.",
 "pista1": "El paso es válido, pero no en este estado: conflicto con un código de transición.",
 "pista2": "409 y 'INVALID_STATE_TRANSITION'",
 "feature": TRANSFER_BG + r"""
  Scenario: Confirmar demasiado pronto
    * def t = call read('_traspaso-listo.feature') { gestora: 'FJORDLIV', valor: '80000.00', dias: 2, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'FJORDLIV', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    Given path '/api/v1/transfers', response.id, 'confirm'
    And headers t.erik.auth
    When method post
    Then status ⟦409⟧
    And match response.code == ⟦'INVALID_STATE_TRANSITION'⟧
"""},
{"id": "E03", "title": "Adelanta el reloj",
 "enunciado": "La gestora tarda 2 días. Congela el reloj 2 días después (mismo día a las 10:30) y completa el estado de la saga.",
 "pista1": "El ayudante congeló el martes 2026-10-20 a las 10:00. Dos días después es jueves 22.",
 "pista2": "'2026-10-22T10:30:00' y 'VALUED'",
 "feature": TRANSFER_BG + r"""
  Scenario: Pasan dos días
    * def t = call read('_traspaso-listo.feature') { gestora: 'NORRBO', valor: '95000.00', dias: 2, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'NORRBO', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    * def id = response.id
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: ⟦'2026-10-22T10:30:00'⟧ }
    When method post
    Then status 200
    # Dos días después el token de antes ha caducado (también en tiempo virtual): login otra vez
    * def erik = call read('_login.feature') usuarios.erik
    Given url transfersUrl
    And path '/api/v1/transfers', id
    And headers erik.auth
    When method get
    Then status 200
    And match response.status == ⟦'VALUED'⟧
    And match response.valuedAmount == '95000.00'
"""},
{"id": "E04", "title": "Aprueba Operaciones",
 "enunciado": "Con demora 0 la valoración llega al momento. Confirma y aprueba. Completa quién aprueba y el estado final.",
 "pista1": "Aprobar es cosa de operaciones.olof (t.olof). Con desviación 0 el importe cuadra.",
 "pista2": "t.olof.auth y 'COMPLETED'",
 "feature": TRANSFER_BG + r"""
  Scenario: Aprobado
    * def t = call read('_traspaso-listo.feature') { gestora: 'SUNDPENSION', valor: '60000.00', dias: 0, desviacion: 0 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'SUNDPENSION', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    And match response.status == 'VALUED'
    * def id = response.id
    Given path '/api/v1/transfers', id, 'confirm'
    And headers t.erik.auth
    When method post
    Then status 200
    Given path '/api/v1/transfers', id, 'approve'
    And headers ⟦t.olof.auth⟧
    When method post
    Then status 200
    And match response.status == ⟦'COMPLETED'⟧
    And match response.contributionId == '#regex ctb_.+'
"""},
{"id": "E05", "title": "Fuera de tolerancia",
 "enunciado": "La gestora manda un 5 % más de lo valorado. Completa el estado al que va la saga y la desviación.",
 "pista1": "Más del 2 %: alguien tiene que revisarlo a mano. La desviación sale con 2 decimales, como texto.",
 "pista2": "'MANUAL_REVIEW' y '5.00'",
 "feature": TRANSFER_BG + r"""
  Scenario: Revisión manual
    * def t = call read('_traspaso-listo.feature') { gestora: 'SUNDPENSION', valor: '50000.00', dias: 0, desviacion: 5 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'SUNDPENSION', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/transfers', id, 'confirm'
    And headers t.erik.auth
    When method post
    Given path '/api/v1/transfers', id, 'approve'
    And headers t.olof.auth
    When method post
    Then status 200
    And match response.status == ⟦'MANUAL_REVIEW'⟧
    And match response.deviationPct == ⟦'5.00'⟧
    And match response.receivedAmount == '52500.00'
"""},
{"id": "E06", "title": "El historial cuenta la saga",
 "enunciado": "Tras aceptar el importe en revisión manual, valida el historial completo. Completa el camino de estados.",
 "pista1": "Empieza esperando la valoración, pasa por valorado, espera aprobación, revisión manual y termina completado.",
 "pista2": "['VALUATION_PENDING', 'VALUED', 'AWAITING_APPROVAL', 'MANUAL_REVIEW', 'COMPLETED']",
 "feature": TRANSFER_BG + r"""
  Scenario: Historial
    * def t = call read('_traspaso-listo.feature') { gestora: 'NORRBO', valor: '40000.00', dias: 0, desviacion: -3 }
    Given url transfersUrl
    And path '/api/v1/transfers'
    And headers t.erik.auth
    And request { customerId: '#(t.customerId)', targetPlanId: '#(t.planId)', providerCode: 'NORRBO', policyNumber: '#(t.poliza)' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v1/transfers', id, 'confirm'
    And headers t.erik.auth
    When method post
    Given path '/api/v1/transfers', id, 'approve'
    And headers t.olof.auth
    When method post
    Given path '/api/v1/transfers', id, 'accept-amount'
    And headers t.olof.auth
    When method post
    Then status 200
    And match response.history[*].to == ⟦['VALUATION_PENDING', 'VALUED', 'AWAITING_APPROVAL', 'MANUAL_REVIEW', 'COMPLETED']⟧
"""},
],
}

LECCIONES = [L22, L23, L24, L25, L26, L27]

# Los docstrings de Karate usan tres comillas dobles: aquí se escriben TRIPLE para no romper las cadenas de Python.
def _triple(x):
    if isinstance(x, str): return x.replace("TRIPLE", '"' * 3)
    if isinstance(x, dict): return {k: _triple(v) for k, v in x.items()}
    if isinstance(x, list): return [_triple(v) for v in x]
    return x
LECCIONES = [_triple(l) for l in LECCIONES]
