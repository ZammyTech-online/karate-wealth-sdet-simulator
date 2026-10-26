# Tramo 4 · Lecciones L28-L33 sobre la oleada 4 (sistemas heredados, OAuth2 de máquinas, contratos, calidad y proyecto final).
# Necesitan el perfil completo:  nordvik up -Profile completo
from lecciones_d import LOGIN
from lecciones_e import CLIENTE

RESET_RELOJ = r"""
  Background:
    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }
"""

PLAN = r"""
# Ayudante: un cliente nuevo de asesor.erik con un plan de pensión PRIVATE. Devuelve customerId, planId y los logins de erik y olof.
# Uso:  * def p = call read('_plan.feature')
@ignore
Feature: Ayudante · cliente con plan de pensión

  Scenario: Plan
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
"""

PERSONA = r"""
# Ayudante: el número nacional de una persona sintética de la edad que digas.
# Uso:  * def p = call read('_persona.feature') { edad: 66 }   →   p.pnr
@ignore
Feature: Ayudante · persona de una edad

  Scenario: Persona
    Given url personUrl
    And path '/api/v1/persons'
    And request { age: '#(edad)' }
    When method post
    Then status 200
    * def pnr = response.persons[0].nationalId
"""

SOBRE = r"""
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
  <soapenv:Body>
    <leg:GetLegacyPlansRequest>
      <leg:personalNumber>#(pnr)</leg:personalNumber>
    </leg:GetLegacyPlansRequest>
  </soapenv:Body>
</soapenv:Envelope>
"""

def sobre(indent="    "):
    lines = SOBRE.strip("\n").split("\n")
    return "\n".join([indent + "And request", indent + '  """'] + [indent + "  " + l for l in lines] + [indent + '  """'])

# ───────────────────────────── L28 ─────────────────────────────
L28 = {
"code": "L28", "slug": "xml-y-soap", "url": "soapUrl",
"readme": r"""
# L28 · XML y SOAP: hablar con el sistema de pensiones antiguo

**Tramo 4.** Ya tienes la empresa completa. Arranca el perfil grande:

```
nordvik up -Profile completo
```

## La idea

Los planes de pensión anteriores a 2015 viven en un sistema antiguo, `legacy-pensions-soap` (7510), que solo habla
**SOAP 1.1**: un sobre XML, la cabecera `SOAPAction` y los errores como **SOAP Fault con HTTP 500**.
Karate lee y escribe XML de forma nativa:

```gherkin
Given url soapUrl
And path '/soap/LegacyPensionService'
And request
  TRIPLE
  <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
    <soapenv:Body>
      <leg:GetLegacyPlansRequest><leg:personalNumber>#(pnr)</leg:personalNumber></leg:GetLegacyPlansRequest>
    </soapenv:Body>
  </soapenv:Envelope>
  TRIPLE
When soap action 'GetLegacyPlans'
Then status 200
And match /Envelope/Body/GetLegacyPlansResponse/count == '2'
```

- `soap action 'X'` pone la cabecera SOAPAction, el método POST y el Content-Type `text/xml`.
- `#(pnr)` funciona también dentro de XML.
- Las rutas XPath (`/Envelope/Body/...`) ignoran los prefijos de espacio de nombres (`soapenv:`, `leg:`).
- En XML **todo es texto**: el recuento es `'2'`, no `2`.
- `karate.xmlPath(response, '//plan[1]/planNumber')` saca un valor para usarlo después.

## La norma

1. Un 500 en SOAP no significa "caída": lee `faultcode` y `detail/errorCode`.
2. Valida el WSDL (`?wsdl`): es el contrato del servicio.
3. Solo las personas nacidas antes de 1975 tienen planes antiguos: prepara tus datos con la edad adecuada.

## El fundamento

Las empresas grandes no migran todo a la vez. Un SDET senior prueba REST, mensajería y SOAP en la misma suite.
""",
"ejemplo": r"""
# Ejemplo resuelto de L28. Ejecútalo con:  academia ejemplo L28
@ejemplo
Feature: L28 · Planes antiguos por SOAP

  Scenario: Listar los planes de una persona de 66 años
    * def p = call read('ejercicios/_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
""" + sobre() + r"""
    When soap action 'GetLegacyPlans'
    Then status 200
    And match /Envelope/Body/GetLegacyPlansResponse/count == '#notnull'
    * def numero = karate.xmlPath(response, '//plan[1]/planNumber')
    * match numero == '#regex LP-\\d{4}-[0-9A-F]{4}'
""",
"apoyo": {"_persona.feature": PERSONA},
"ejercicios": [
{"id": "E01", "title": "Tu primera petición SOAP",
 "enunciado": "Pide los planes de una persona de 66 años. Completa la palabra clave que manda la acción SOAP y el estado.",
 "pista1": "Karate tiene un paso especial para SOAP: soap action 'Nombre'. Si todo va bien, 200.",
 "pista2": "soap action y 200",
 "feature": r"""
  Scenario: Listar
    * def p = call read('_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
""" + sobre() + r"""
    When ⟦soap action⟧ 'GetLegacyPlans'
    Then status ⟦200⟧
"""},
{"id": "E02", "title": "En XML todo es texto",
 "enunciado": "Una persona de 36 años no tiene planes antiguos. Completa la ruta XPath del recuento y el valor esperado.",
 "pista1": "La ruta va desde Envelope, sin prefijos. El número sale como texto.",
 "pista2": "/Envelope/Body/GetLegacyPlansResponse/count y '0'",
 "feature": r"""
  Scenario: Sin planes
    * def p = call read('_persona.feature') { edad: 36 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
""" + sobre() + r"""
    When soap action 'GetLegacyPlans'
    Then status 200
    And match ⟦/Envelope/Body/GetLegacyPlansResponse/count⟧ == ⟦'0'⟧
"""},
{"id": "E03", "title": "Un Fault no es una caída",
 "enunciado": "Con un número nacional inválido el servicio responde un SOAP Fault. Completa el estado HTTP y el código de error del detail.",
 "pista1": "SOAP 1.1 manda los Faults con HTTP 500. El código está en detail/errorCode.",
 "pista2": "500 y 'INVALID_PNR'",
 "feature": r"""
  Scenario: Número inválido
    * def pnr = '19600505-000X'
    Given url soapUrl
    And path '/soap/LegacyPensionService'
""" + sobre() + r"""
    When soap action 'GetLegacyPlans'
    Then status ⟦500⟧
    And match /Envelope/Body/Fault/faultcode == 'soapenv:Client'
    And match /Envelope/Body/Fault/detail/errorCode == ⟦'INVALID_PNR'⟧
"""},
{"id": "E04", "title": "Sin SOAPAction",
 "enunciado": "Manda el mismo sobre con method post (sin soap action). Completa la cabecera de tipo de contenido y el código de error.",
 "pista1": "Sin soap action tienes que poner tú el Content-Type de XML. El código dice que falta la acción.",
 "pista2": "'text/xml' y 'NO_SOAPACTION'",
 "feature": r"""
  Scenario: Falta la acción
    * def p = call read('_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And header Content-Type = ⟦'text/xml'⟧
""" + sobre() + r"""
    When method post
    Then status 500
    And match /Envelope/Body/Fault/detail/errorCode == ⟦'NO_SOAPACTION'⟧
"""},
{"id": "E05", "title": "Encadenar dos llamadas SOAP",
 "enunciado": "Saca el número del primer plan y pide ese plan en concreto. Completa la expresión XPath y la acción de la segunda llamada.",
 "pista1": "karate.xmlPath(response, '...') con una ruta que empiece por // y el primer plan. La segunda acción es GetLegacyPlan (singular).",
 "pista2": "'//plan[1]/planNumber' y 'GetLegacyPlan'",
 "feature": r"""
  Scenario: Primero la lista, luego el plan
    * def p = call read('_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
""" + sobre() + r"""
    When soap action 'GetLegacyPlans'
    Then status 200
    * def numero = karate.xmlPath(response, ⟦'//plan[1]/planNumber'⟧)
    * def valor = karate.xmlPath(response, '//plan[1]/value')
    Given path '/soap/LegacyPensionService'
    And request
      TRIPLE
      <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
        <soapenv:Body>
          <leg:GetLegacyPlanRequest>
            <leg:personalNumber>#(pnr)</leg:personalNumber>
            <leg:planNumber>#(numero)</leg:planNumber>
          </leg:GetLegacyPlanRequest>
        </soapenv:Body>
      </soapenv:Envelope>
      TRIPLE
    When soap action ⟦'GetLegacyPlan'⟧
    Then status 200
    And match /Envelope/Body/GetLegacyPlanResponse/plan/value == valor
"""},
{"id": "E06", "title": "El contrato: el WSDL",
 "enunciado": "Pide el WSDL y comprueba que declara la acción GetLegacyPlans. Completa el parámetro de la URL y el operador.",
 "pista1": "El WSDL se pide con ?wsdl: un parámetro sin valor. Para buscar un texto dentro de otro, contains.",
 "pista2": "'wsdl' y contains",
 "feature": r"""
  Scenario: WSDL
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And param ⟦wsdl⟧ = ''
    When method get
    Then status 200
    # 'string' convierte el XML de la respuesta en su texto (karate.toString lo convertiría en JSON)
    * string texto = response
    * match texto ⟦contains⟧ 'soapAction="GetLegacyPlans"'
"""},
],
}

# ───────────────────────────── L29 ─────────────────────────────
L29 = {
"code": "L29", "slug": "ficheros-de-ancho-fijo", "url": "mainframeUrl",
"readme": r"""
# L29 · Ficheros de ancho fijo: el mainframe

## La idea

`legacy-mainframe-bridge` (7509) traduce entre Nordvik y un mainframe que habla en **líneas de 80 columnas exactas**,
sin separadores: cada campo ocupa unas posiciones fijas (`GET /api/v1/layouts`), el texto se rellena con espacios a la
derecha y los números con ceros a la izquierda y 2 decimales implícitos (`000000012345678` = 123456.78). Los errores son
**códigos de retorno** numéricos (`rc`): `0000` bien, `0016` fuera de la ventana de lote, `0042` fecha imposible...

En Karate construyes y lees esas líneas con funciones JavaScript:

```gherkin
* def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
* def linea = pad('BL' + pad(planId, 20) + '000000012345678' + '20141231', 80)
Given url mainframeUrl
And path '/api/v1/inbound/balances'
And header Content-Type = 'text/plain'
And request linea
When method post
Then status 200
And match response.results[0].rc == '0000'
```

El lote nocturno solo se puede lanzar de **02:00 a 04:00**: usa el reloj virtual (`/api/v1/clock/freeze`) y devuélvelo
con `afterScenario`.

## La norma

1. Comprueba la **longitud exacta** de cada línea: un carácter de más desplaza todos los campos (trampa T21).
2. Compara los campos de texto con `trim()`.
3. Un `rc` distinto de `0000` no siempre es un error: `0004` es un aviso.
""",
"ejemplo": r"""
# Ejemplo resuelto de L29. Ejecútalo con:  academia ejemplo L29
@ejemplo
Feature: L29 · Lote nocturno y fichero exportado
""" + RESET_RELOJ + r"""
  Scenario: Exportar a las 02:30 y comprobar las 80 columnas
    * def p = call read('ejercicios/_plan.feature')
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T02:30:00' }
    When method post
    Then status 200
    * def olof = call read('ejercicios/_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status 200
    And match response.rc == '0000'
    Given path '/api/v1/batches/2026-10-21/export'
    And headers olof.auth
    When method get
    Then status 200
    # Ojo: trim() sobre el fichero entero se comería el relleno de la última línea (trampa T21)
    * def lineas = karate.filter(response.split('\n'), function(x){ return x.length > 0 })
    * def todasDe80 = function(ls){ for (var i = 0; i < ls.length; i++) if (ls[i].length != 80) return false; return true }
    * assert todasDe80(lineas)
    * match lineas[0] contains 'HD20261021'
""",
"apoyo": {"_login.feature": LOGIN, "_cliente.feature": CLIENTE, "_plan.feature": PLAN},
"ejercicios": [
{"id": "E01", "title": "El diseño de registro",
 "enunciado": "Pide los diseños de registro. Completa el número mínimo de tipos de línea y el tipo de la primera.",
 "pista1": "Hay cabecera, plan, cola y saldo: cuatro. La primera es la cabecera (HD).",
 "pista2": "4 y 'HD'",
 "feature": r"""
  Scenario: Layouts
    Given url mainframeUrl
    And path '/api/v1/layouts'
    When method get
    Then status 200
    And assert response.length >= ⟦4⟧
    And match response[0].record == ⟦'HD'⟧
"""},
{"id": "E02", "title": "Fuera de la ventana de lote",
 "enunciado": "A las 10:00 el lote no puede correr. Completa el estado y el código de retorno.",
 "pista1": "Es un conflicto con el estado del sistema. El rc de la ventana cerrada está en la tabla del README.",
 "pista2": "409 y '0016'",
 "feature": RESET_RELOJ + r"""
  Scenario: Ventana cerrada
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T10:00:00' }
    When method post
    * def olof = call read('_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status ⟦409⟧
    And match response.rc == ⟦'0016'⟧
"""},
{"id": "E03", "title": "Dentro de la ventana",
 "enunciado": "Congela el reloj a las 02:30 y lanza el lote. Completa la hora y el código de retorno.",
 "pista1": "La ventana es de 02:00 a 04:00. Todo bien es cuatro ceros.",
 "pista2": "'2026-10-21T02:30:00' y '0000'",
 "feature": RESET_RELOJ + r"""
  Scenario: Lote nocturno
    * def p = call read('_plan.feature')
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: ⟦'2026-10-21T02:30:00'⟧ }
    When method post
    Then status 200
    * def olof = call read('_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status 200
    And match response.rc == ⟦'0000'⟧
"""},
{"id": "E04", "title": "Contar columnas",
 "enunciado": "Comprueba que la cola (última línea) dice cuántos planes hay. Completa la posición donde empieza el número (desde 0) y el total de líneas que no son de plan.",
 "pista1": "La cola es TR + 9 dígitos: el número empieza justo después de las 2 letras. Hay una cabecera y una cola.",
 "pista2": "2 y 2",
 "feature": RESET_RELOJ + r"""
  Scenario: La cola cuadra
    * def p = call read('_plan.feature')
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-21T03:00:00' }
    When method post
    * def olof = call read('_login.feature') usuarios.olof
    Given url mainframeUrl
    And path '/api/v1/batches/run'
    And headers olof.auth
    When method post
    Then status 200
    Given path '/api/v1/batches/2026-10-21/export'
    And headers olof.auth
    When method get
    Then status 200
    # Ojo: trim() sobre el fichero entero se comería el relleno de la última línea (trampa T21)
    * def lineas = karate.filter(response.split('\n'), function(x){ return x.length > 0 })
    * def cola = lineas[lineas.length - 1]
    * def registros = parseInt(cola.substring(⟦2⟧, 11), 10)
    * match registros == lineas.length - ⟦2⟧
"""},
{"id": "E05", "title": "Mandar una línea al mainframe",
 "enunciado": "Construye una línea BL de 80 columnas con el saldo 123456.78 del plan. Completa la longitud total y el código esperado.",
 "pista1": "Toda línea mide lo mismo que el ancho del fichero. Si todo cuadra, cuatro ceros.",
 "pista2": "80 y '0000'",
 "feature": r"""
  Scenario: Saldo heredado
    * def p = call read('_plan.feature')
    * def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
    * def linea = pad('BL' + pad(p.planId, 20) + '000000012345678' + '20141231', ⟦80⟧)
    Given url mainframeUrl
    And path '/api/v1/inbound/balances'
    And headers p.olof.auth
    And header Content-Type = 'text/plain'
    And request linea
    When method post
    Then status 200
    And match response.results[0].rc == ⟦'0000'⟧
    Given path '/api/v1/legacy-balances', p.planId
    And headers p.olof.auth
    When method get
    Then match response.balance.amount == '123456.78'
"""},
{"id": "E06", "title": "Una fecha imposible",
 "enunciado": "El mes 13 no existe. Completa la fecha mala (AAAAMMDD) y el código de retorno.",
 "pista1": "Cualquier fecha con mes 13 vale. El rc de fecha inválida está en GET /api/v1/return-codes.",
 "pista2": "'20141301' y '0042'",
 "feature": r"""
  Scenario: Fecha imposible
    * def p = call read('_plan.feature')
    * def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
    * def linea = pad('BL' + pad(p.planId, 20) + '000000012345678' + ⟦'20141301'⟧, 80)
    Given url mainframeUrl
    And path '/api/v1/inbound/balances'
    And headers p.olof.auth
    And header Content-Type = 'text/plain'
    And request linea
    When method post
    Then status 200
    And match response.results[0].rc == ⟦'0042'⟧
"""},
],
}

# ───────────────────────────── L30 ─────────────────────────────
L30 = {
"code": "L30", "slug": "oauth2-entre-maquinas", "url": "insurerSimUrl",
"readme": r"""
# L30 · OAuth2 entre máquinas: la aseguradora

## La idea

La aseguradora (simulador E11, 7711) no conoce a tus usuarios: autentica **sistemas**. Nordvik pide un token con el
flujo **client credentials** (sin usuario, con `client_id` y `client_secret`) y lo manda como `Bearer`:

```gherkin
Given url insurerSimUrl
And path '/oauth/token'
And form field grant_type = 'client_credentials'
And form field client_id = 'nordvik'
And form field client_secret = 'e11-insurer-client-secret-2026'
When method post
Then status 200
* def token = response.access_token
```

El token **caduca** (300 s en el reloj virtual) y la aseguradora puede invalidarlos todos de golpe
(`POST /__admin/tokens/expire-all`). Un cliente bien hecho pide uno nuevo y repite **una sola vez**; es lo que hace
`insurer-adapter` (7508), y lo puedes ver en `GET /api/v1/token-status`.

Con el token pides la **suscripción médica**: `POST /v1/underwriting` con edad, fumador, IMC y enfermedades →
STANDARD, RATED (recargo del 25/50/75 %) o DECLINED. La aseguradora **no guarda las respuestas** (`answersRetained: false`).

## La norma

1. Los errores de OAuth2 no son *problems* de Nordvik: vienen como `{ "error": "invalid_client" }`.
2. Prueba que tu cliente sobrevive a un token caducado: invalídalos y repite.
3. El secreto nunca va en el código del test: en una empresa real sale de un almacén de secretos (aquí, `secrets-service`).
""",
"ejemplo": r"""
# Ejemplo resuelto de L30. Ejecútalo con:  academia ejemplo L30
@ejemplo
Feature: L30 · Token de máquina y suscripción

  Scenario: Token y suscripción de una persona sana
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    Then status 200
    * def token = response.access_token
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + token
    And request { applicantRef: 'karate', age: 40, smoker: false, bmi: 23, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status 200
    And match response contains { decision: 'STANDARD', loadingPct: 0, answersRetained: false }
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Client credentials",
 "enunciado": "Pide un token de máquina. Completa el tipo de concesión y cuánto dura el token.",
 "pista1": "El flujo se llama igual que el tipo de credencial. La duración está en el README (en segundos).",
 "pista2": "'client_credentials' y 300",
 "feature": r"""
  Scenario: Token
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = ⟦'client_credentials'⟧
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    Then status 200
    And match response contains { token_type: 'Bearer', expires_in: ⟦300⟧ }
"""},
{"id": "E02", "title": "Secreto equivocado",
 "enunciado": "Con un client_secret malo la aseguradora no te conoce. Completa estado y error.",
 "pista1": "No sabe quién eres: 401. El error OAuth2 estándar para credenciales de cliente malas es invalid_client.",
 "pista2": "401 y 'invalid_client'",
 "feature": r"""
  Scenario: Cliente desconocido
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'no-es-este'
    When method post
    Then status ⟦401⟧
    And match response.error == ⟦'invalid_client'⟧
"""},
{"id": "E03", "title": "Fumador con sobrepeso",
 "enunciado": "Un fumador (2 puntos) con IMC 31 (1 punto) sale con recargo. Completa la decisión y el recargo.",
 "pista1": "1 punto es +25 %, 2 o 3 puntos son +50 %.",
 "pista2": "'RATED' y 50",
 "feature": r"""
  Scenario: Suscripción con recargo
    Given url insurerSimUrl
    And path '/oauth/token'
    And form field grant_type = 'client_credentials'
    And form field client_id = 'nordvik'
    And form field client_secret = 'e11-insurer-client-secret-2026'
    When method post
    * def token = response.access_token
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + token
    And request { applicantRef: 'karate', age: 45, smoker: true, bmi: 31, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status 200
    And match response.decision == ⟦'RATED'⟧
    And match response.loadingPct == ⟦50⟧
"""},
{"id": "E04", "title": "Sin token",
 "enunciado": "Llama a la suscripción sin Authorization. Completa estado y error.",
 "pista1": "Falta el token: 401. El error OAuth2 para un token ausente o malo es invalid_token.",
 "pista2": "401 y 'invalid_token'",
 "feature": r"""
  Scenario: Anónimo
    Given url insurerSimUrl
    And path '/v1/underwriting'
    And request { applicantRef: 'karate', age: 40, bmi: 22 }
    When method post
    Then status ⟦401⟧
    And match response.error == ⟦'invalid_token'⟧
"""},
{"id": "E05", "title": "El token caducado",
 "enunciado": "Invalida todos los tokens y repite la llamada con el viejo; luego pide otro. Completa el estado con el token viejo y con el nuevo.",
 "pista1": "Con el viejo ya no entras; con uno recién pedido, sí.",
 "pista2": "401 y 200",
 "feature": r"""
  Scenario: Renovar
    * def pedir = function(){ var r = karate.http(insurerSimUrl + '/oauth/token').header('Content-Type', 'application/x-www-form-urlencoded').post('grant_type=client_credentials&client_id=nordvik&client_secret=e11-insurer-client-secret-2026'); return r.body.access_token }
    * def viejo = pedir()
    Given url insurerSimUrl
    And path '/__admin/tokens/expire-all'
    When method post
    Then status 200
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + viejo
    And request { applicantRef: 'karate', age: 40, smoker: false, bmi: 23, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status ⟦401⟧
    * def nuevo = pedir()
    Given path '/v1/underwriting'
    And header Authorization = 'Bearer ' + nuevo
    And request { applicantRef: 'karate', age: 40, smoker: false, bmi: 23, conditions: [], hospitalizedLast5y: false }
    When method post
    Then status ⟦200⟧
"""},
{"id": "E06", "title": "Lo que hace Nordvik por dentro",
 "enunciado": "El adaptador de Nordvik nunca enseña su token. Completa el campo que dice si tiene uno y el operador que comprueba que el token no aparece.",
 "pista1": "Mira GET /api/v1/token-status: un booleano dice si hay token. Para 'no contiene', la negación de contains.",
 "pista2": "hasToken y !contains",
 "feature": r"""
  Scenario: Estado del token del adaptador
    * def olof = call read('_login.feature') usuarios.olof
    Given url insurerAdapterUrl
    And path '/api/v1/token-status'
    And headers olof.auth
    When method get
    Then status 200
    And match response.⟦hasToken⟧ == '#boolean'
    * def texto = karate.toString(response)
    * match texto ⟦!contains⟧ 'ins_'
"""},
],
}

# ───────────────────────────── L31 ─────────────────────────────
L31 = {
"code": "L31", "slug": "contratos-y-esquemas", "url": "contractCheckUrl",
"readme": r"""
# L31 · Contratos y esquemas: que nadie rompa a nadie

## La idea

Un contrato de API (OpenAPI) o de evento (esquema) es una **promesa**. Tres herramientas para vigilarla:

**1 · Un esquema en Karate.** Defines la forma y comparas:

```gherkin
* def fondo = { isin: '#string', name: '#string', base_currency: '#string', sri: '#number', fees: '#[] #object' }
Given url catalogUrl
And path '/api/v1/funds/SE0000000001'
When method get
Then match response contains fondo
```

Ojo: `product-catalog` escribe en **snake_case** (`base_currency`); otros servicios en camelCase. Un esquema por servicio.

**2 · `schema-registry` (6111).** Los esquemas de los eventos tienen versiones y la nueva debe ser compatible
**BACKWARD** con la anterior: no se pueden añadir campos obligatorios, quitar campos ni cambiar tipos (409 `INCOMPATIBLE_SCHEMA`).

**3 · `contract-check` (7603).** Compara el OpenAPI publicado de un servicio con su respuesta real y lista las diferencias.
Con la trampa **T9** activada (`PUT chaos /api/v1/traps/T9 { enabled: true }`), product-catalog renombra `isin` a
`instrumentId` sin tocar su OpenAPI: contract-check lo detecta como `DRIFT`.

## La norma

1. Valida contra el contrato de **cada entorno**: un test que solo mira tu entorno no detecta la deriva.
2. Antes de publicar un evento con un campo nuevo, regístralo en el registro de esquemas.
3. Deja las trampas como estaban: el `Background` de esta lección resetea chaos-control.
""",
"ejemplo": r"""
# Ejemplo resuelto de L31. Ejecútalo con:  academia ejemplo L31
@ejemplo
Feature: L31 · Esquema y deriva

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: El fondo cumple su esquema y su OpenAPI
    * def fondo = { isin: '#string', name: '#string', base_currency: '#string', sri: '#number', fees: '#[] #object' }
    Given url catalogUrl
    And path '/api/v1/funds/SE0000000001'
    When method get
    Then status 200
    And match response contains fondo
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: 'product-catalog', path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == 'MATCHES'
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Un esquema en snake_case",
 "enunciado": "Completa el nombre del campo de moneda base tal como lo escribe product-catalog y el marcador del número de riesgo.",
 "pista1": "Este servicio usa snake_case: palabras en minúscula unidas por _. El riesgo es un número.",
 "pista2": "base_currency y '#number'",
 "feature": r"""
  Scenario: Esquema del fondo
    * def fondo = { isin: '#string', name: '#string', ⟦base_currency⟧: '#string', sri: ⟦'#number'⟧ }
    Given url catalogUrl
    And path '/api/v1/funds/SE0000000002'
    When method get
    Then status 200
    And match response contains fondo
"""},
{"id": "E02", "title": "Registrar la versión 1",
 "enunciado": "Registra el primer esquema de un evento nuevo. Completa quién puede hacerlo y el estado.",
 "pista1": "Registrar esquemas es cosa de administración. Es un recurso nuevo.",
 "pista2": "usuarios.admin y 201",
 "feature": r"""
  Scenario: v1
    * def admin = call read('_login.feature') ⟦usuarios.admin⟧
    * def topic = 'academy.e' + java.util.UUID.randomUUID().toString().substring(0, 6) + '.created'
    Given url schemaRegistryUrl
    And path '/api/v1/subjects', topic, 'versions'
    And headers admin.auth
    And request { schema: { required: ['id'], properties: { id: 'string', amount: 'string' } } }
    When method post
    Then status ⟦201⟧
    And match response.version == 1
"""},
{"id": "E03", "title": "Un campo obligatorio nuevo rompe a los consumidores",
 "enunciado": "La v2 añade currency como obligatorio. Completa el estado y el motivo exacto.",
 "pista1": "Es un conflicto con la versión anterior. El motivo empieza por 'campo obligatorio nuevo: '.",
 "pista2": "409 y 'campo obligatorio nuevo: currency'",
 "feature": r"""
  Scenario: v2 incompatible
    * def admin = call read('_login.feature') usuarios.admin
    * def topic = 'academy.e' + java.util.UUID.randomUUID().toString().substring(0, 6) + '.created'
    Given url schemaRegistryUrl
    And path '/api/v1/subjects', topic, 'versions'
    And headers admin.auth
    And request { schema: { required: ['id'], properties: { id: 'string', amount: 'string' } } }
    When method post
    Then status 201
    Given path '/api/v1/subjects', topic, 'versions'
    And headers admin.auth
    And request { schema: { required: ['id', 'currency'], properties: { id: 'string', amount: 'string', currency: 'string' } } }
    When method post
    Then status ⟦409⟧
    And match response.reasons contains ⟦'campo obligatorio nuevo: currency'⟧
"""},
{"id": "E04", "title": "Validar un payload",
 "enunciado": "Valida un evento sin paymentId contra el esquema de payment.received. Completa si es válido y el error.",
 "pista1": "Le falta un campo obligatorio. El error dice 'falta ' y el campo.",
 "pista2": "false y 'falta paymentId'",
 "feature": r"""
  Scenario: Payload incompleto
    Given url schemaRegistryUrl
    And path '/api/v1/subjects/payment.received/validate'
    And request { payload: { customerId: 'c1', amount: '100.00' } }
    When method post
    Then status 200
    And match response.valid == ⟦false⟧
    And match response.errors contains ⟦'falta paymentId'⟧
"""},
{"id": "E05", "title": "OpenAPI frente a la realidad",
 "enunciado": "Pide a contract-check que compare product-catalog. Completa el servicio y el resultado esperado sin trampas.",
 "pista1": "El servicio se llama por su nombre (no por su puerto). Si todo cuadra, coincide.",
 "pista2": "'product-catalog' y 'MATCHES'",
 "feature": r"""
  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: Sin deriva
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: ⟦'product-catalog'⟧, path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == ⟦'MATCHES'⟧
"""},
{"id": "E06", "title": "La trampa T9",
 "enunciado": "Activa T9 y repite la comprobación. Completa el resultado y el campo nuevo que aparece sin documentar.",
 "pista1": "Ahora no coincide: deriva. El equipo v2 renombró isin a otro nombre (mira el README).",
 "pista2": "'DRIFT' y '$.instrumentId'",
 "feature": r"""
  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post

  Scenario: Deriva detectada
    Given url chaosUrl
    And path '/api/v1/traps/T9'
    And request { enabled: true }
    When method put
    Then status 200
    Given url contractCheckUrl
    And path '/api/v1/checks'
    And request { service: 'product-catalog', path: '/api/v1/funds/SE0000000001' }
    When method post
    Then status 200
    And match response.status == ⟦'DRIFT'⟧
    And match response.differences contains { kind: 'UNDOCUMENTED_FIELD', path: ⟦'$.instrumentId'⟧, expected: '#string' }
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
"""},
],
}

# ───────────────────────────── L32 ─────────────────────────────
RUN = r"""
    * def correr = function(suite, a, b){ return karate.http(testInsightsUrl + '/api/v1/runs').header('Content-Type', 'application/json').post({ suite: suite, results: [ { name: 'A pago', status: a, durationMs: 100 }, { name: 'B login', status: 'passed', durationMs: 40 }, { name: 'C orden', status: b, durationMs: 900 } ] }) }
"""

L32 = {
"code": "L32", "slug": "tests-inestables", "url": "testInsightsUrl",
"readme": r"""
# L32 · Tests inestables: medir la calidad de tu propia suite

## La idea

Un test **inestable** (*flaky*) pasa y falla sin que cambie nada. Es el enemigo número uno de una suite grande: la gente
deja de creer en el rojo. `test-insights` (7604) recibe tus resultados y te dice:

- `GET /api/v1/suites/{suite}/flaky` → los que alternan, con su `flipRate` (alternancias / transiciones posibles) y su
  historia (`PFPF`).
- `GET /api/v1/suites/{suite}/trend` → la tasa de éxito por ejecución y si mejora o empeora.
- `GET /api/v1/suites/{suite}/slowest` → los más lentos de media.

Mandas cada ejecución así:

```gherkin
Given url testInsightsUrl
And path '/api/v1/runs'
And request { suite: 'mi-suite', results: [ { name: 'L16 E01', status: 'passed', durationMs: 340 } ] }
When method post
Then status 201
```

`karate.http(url).post(cuerpo)` hace una llamada desde JavaScript: útil para meter varias ejecuciones en un bucle.

## La norma

1. Un test inestable se **aísla** (etiqueta y ejecución aparte) y se arregla; no se reintenta hasta que pase.
2. Mira la tendencia, no una ejecución suelta.
3. Usa nombres de test únicos por ejecución (si no, 422 `DUPLICATE_TEST_NAMES`).
""",
"ejemplo": r"""
# Ejemplo resuelto de L32. Ejecútalo con:  academia ejemplo L32
@ejemplo
Feature: L32 · Detectar un test inestable

  Scenario: Cuatro ejecuciones y el informe
    * def suite = 'ejemplo-' + java.util.UUID.randomUUID().toString().substring(0, 6)
""" + RUN + r"""
    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'passed', 'failed')
    * correr(suite, 'failed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'flaky'
    When method get
    Then status 200
    And match response.items[0] contains { name: 'A pago', history: 'PFPF', flipRate: 1.0 }
""",
"apoyo": {},
"ejercicios": [
{"id": "E01", "title": "Tu primera ejecución",
 "enunciado": "Registra una ejecución con un resultado. Completa el estado y el número de ejecución de una suite nueva.",
 "pista1": "Es un recurso nuevo. La primera ejecución de una suite es la número 1.",
 "pista2": "201 y 1",
 "feature": r"""
  Scenario: Una ejecución
    * def suite = 'e01-' + java.util.UUID.randomUUID().toString().substring(0, 6)
    Given url testInsightsUrl
    And path '/api/v1/runs'
    And request { suite: '#(suite)', results: [ { name: 'L16 E01', status: 'passed', durationMs: 340 } ] }
    When method post
    Then status ⟦201⟧
    And match response.runNumber == ⟦1⟧
"""},
{"id": "E02", "title": "¿Quién alterna?",
 "enunciado": "Tras cuatro ejecuciones, A pago alterna en cada una. Completa su historia y su tasa de alternancia.",
 "pista1": "P es passed y F failed, en orden. Si cambia en las 3 transiciones posibles, la tasa es 1.",
 "pista2": "'PFPF' y 1.0",
 "feature": r"""
  Scenario: Flaky
    * def suite = 'e02-' + java.util.UUID.randomUUID().toString().substring(0, 6)
""" + RUN + r"""
    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'passed', 'failed')
    * correr(suite, 'failed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'flaky'
    When method get
    Then status 200
    And match response.items[0].name == 'A pago'
    And match response.items[0].history == ⟦'PFPF'⟧
    And match response.items[0].flipRate == ⟦1.0⟧
"""},
{"id": "E03", "title": "Lo estable no es inestable",
 "enunciado": "B login pasa siempre. Completa el operador que comprueba que no aparece en la lista de inestables.",
 "pista1": "Quieres decir 'la lista NO contiene un elemento con ese nombre'.",
 "pista2": "!contains",
 "feature": r"""
  Scenario: Estable
    * def suite = 'e03-' + java.util.UUID.randomUUID().toString().substring(0, 6)
""" + RUN + r"""
    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'passed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'flaky'
    When method get
    Then status 200
    And match response.items[*].name ⟦!contains⟧ 'B login'
"""},
{"id": "E04", "title": "La tendencia",
 "enunciado": "Tres ejecuciones cada vez peores. Completa la dirección de la tendencia y la tasa de éxito de la primera.",
 "pista1": "Si la última es peor que la primera, empeora. La primera pasó todo: 100 %.",
 "pista2": "'WORSENING' y 100.0",
 "feature": r"""
  Scenario: Empeora
    * def suite = 'e04-' + java.util.UUID.randomUUID().toString().substring(0, 6)
""" + RUN + r"""
    * correr(suite, 'passed', 'passed')
    * correr(suite, 'failed', 'passed')
    * correr(suite, 'failed', 'failed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, 'trend'
    When method get
    Then status 200
    And match response.direction == ⟦'WORSENING'⟧
    And match response.runs[0].passRate == ⟦100.0⟧
"""},
{"id": "E05", "title": "El más lento",
 "enunciado": "Completa el nombre del test más lento y el recurso de la URL que lo da.",
 "pista1": "C orden tarda 900 ms. El recurso es el superlativo de lento en inglés.",
 "pista2": "'C orden' y 'slowest'",
 "feature": r"""
  Scenario: Lentos
    * def suite = 'e05-' + java.util.UUID.randomUUID().toString().substring(0, 6)
""" + RUN + r"""
    * correr(suite, 'passed', 'passed')
    Given url testInsightsUrl
    And path '/api/v1/suites', suite, ⟦'slowest'⟧
    When method get
    Then status 200
    And match response.items[0].name == ⟦'C orden'⟧
"""},
{"id": "E06", "title": "Nombres repetidos",
 "enunciado": "Dos resultados con el mismo nombre en una ejecución no se aceptan. Completa estado y código.",
 "pista1": "El cuerpo es válido como JSON, pero no tiene sentido: 422. El código habla de nombres duplicados.",
 "pista2": "422 y 'DUPLICATE_TEST_NAMES'",
 "feature": r"""
  Scenario: Duplicados
    Given url testInsightsUrl
    And path '/api/v1/runs'
    And request { suite: 'dup', results: [ { name: 'X', status: 'passed' }, { name: 'X', status: 'failed' } ] }
    When method post
    Then status ⟦422⟧
    And match response.code == ⟦'DUPLICATE_TEST_NAMES'⟧
"""},
],
}

# ───────────────────────────── L33 ─────────────────────────────
L33 = {
"code": "L33", "slug": "proyecto-final-derecho-al-olvido", "url": "erasureUrl",
"readme": r"""
# L33 · Proyecto final: el derecho al olvido de punta a punta

## La idea

Juntas todo lo aprendido en una saga real que cruza cinco servicios. Un cliente pide que le borren (RGPD art. 17):

1. `erasure-orchestrator` (6507) comprueba lo que lo impide: planes de pensión vivos, carteras con dinero, seguros.
   Si hay algo → `BLOCKED` con `blockers` y **no toca nada**.
2. Si no, tritura las claves del cliente en `key-management`: `pii-vault` recibe el evento `key.shredded` y deja sus
   tokens **ERASED** (consistencia eventual: espera con `retry until`).
3. Retira sus consentimientos (`consent-ledger`) y cierra su ficha (`customer-registry`).
4. Devuelve `COMPLETED`, un certificado SHA-256 y lo que la ley obliga a guardar (`retained`).

Lo pide Cumplimiento (compliance.karin) o el propio cliente.

## La norma

1. Una saga se prueba por **sus efectos en cada servicio**, no solo por la respuesta del orquestador.
2. Prueba el camino bloqueado: el orquestador no debe haber cambiado nada.
3. Repetir la operación es parte del contrato: el segundo borrado responde 409.

## Tu entrega

Los seis ejercicios de esta lección forman la suite de regresión del derecho al olvido. Cuando pasen todos, ejecútalos
diez veces seguidas (`academia corregir L33` varias veces) y manda los resultados a `test-insights` como en L32:
si alguno alterna, tienes un test inestable que arreglar.
""",
"ejemplo": r"""
# Ejemplo resuelto de L33. Ejecútalo con:  academia ejemplo L33
@ejemplo
Feature: L33 · Borrado completo

  Scenario: Tokenizar, borrar y comprobar el efecto
    * def c = call read('ejercicios/_cliente.feature')
    * def karin = call read('ejercicios/_login.feature') usuarios.karin
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    * def iban = response.electronic
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'IBAN', value: '#(iban)', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    * def token = response.token
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    And match response.status == 'COMPLETED'
    * configure retry = { count: 30, interval: 300 }
    Given url piiVaultUrl
    And path '/api/v1/tokens', token
    And headers karin.auth
    And retry until response.status == 'ERASED'
    When method get
    Then status 200
""",
"apoyo": {"_login.feature": LOGIN, "_cliente.feature": CLIENTE, "_plan.feature": PLAN},
"ejercicios": [
{"id": "E01", "title": "Pedir el borrado",
 "enunciado": "Karin pide el borrado de un cliente sin contratos. Completa el estado HTTP y el estado de la solicitud.",
 "pista1": "Se crea la solicitud. Si nada lo impide, termina en el momento.",
 "pista2": "201 y 'COMPLETED'",
 "feature": r"""
  Scenario: Borrado
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status ⟦201⟧
    And match response.status == ⟦'COMPLETED'⟧
    And match response.certificateSha256 == '#regex [0-9a-f]{64}'
"""},
{"id": "E02", "title": "El primer paso de la saga",
 "enunciado": "Completa el nombre del primer paso (triturar las claves) y si salió bien.",
 "pista1": "Triturar claves en inglés de criptografía: CRYPTO_SHRED. ok es un booleano.",
 "pista2": "'CRYPTO_SHRED' y true",
 "feature": r"""
  Scenario: Pasos
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    And match response.steps[0].step == ⟦'CRYPTO_SHRED'⟧
    And match response.steps[0].ok == ⟦true⟧
"""},
{"id": "E03", "title": "El efecto en la ficha",
 "enunciado": "Después del borrado la ficha queda cerrada. Completa la URL del servicio de clientes y el estado de la ficha.",
 "pista1": "La ficha vive en customer-registry (customerUrl). Cerrada en inglés.",
 "pista2": "customerUrl y 'CLOSED'",
 "feature": r"""
  Scenario: Ficha cerrada
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    Given url ⟦customerUrl⟧
    And path '/api/v1/internal/customers', c.customerId
    When method get
    Then status 200
    And match response.status == ⟦'CLOSED'⟧
"""},
{"id": "E04", "title": "El efecto en pii-vault (eventual)",
 "enunciado": "El token del cliente acaba ERASED por un evento. Completa la condición de espera.",
 "pista1": "Hay que esperar a que el estado del token cambie: retry until con la condición.",
 "pista2": "response.status == 'ERASED'",
 "feature": r"""
  Scenario: Token borrado
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url piiVaultUrl
    And path '/api/v1/tokens'
    And headers karin.auth
    And request { type: 'EMAIL', value: '#(c.customerId.substring(0, 8) + "@example.com")', customerId: '#(c.customerId)' }
    When method post
    Then status 201
    * def token = response.token
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    * configure retry = { count: 30, interval: 300 }
    Given url piiVaultUrl
    And path '/api/v1/tokens', token
    And headers karin.auth
    And retry until ⟦response.status == 'ERASED'⟧
    When method get
    Then status 200
"""},
{"id": "E05", "title": "Dos veces no",
 "enunciado": "Pide el borrado del mismo cliente otra vez. Completa estado y código.",
 "pista1": "Ya está hecho: conflicto. El código dice que ya se borró.",
 "pista2": "409 y 'ALREADY_ERASED'",
 "feature": r"""
  Scenario: Repetido
    * def c = call read('_cliente.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Primera vez' }
    When method post
    Then status 201
    Given path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(c.customerId)', reason: 'Segunda vez' }
    When method post
    Then status ⟦409⟧
    And match response.code == ⟦'ALREADY_ERASED'⟧
"""},
{"id": "E06", "title": "Bloqueado por un plan de pensión",
 "enunciado": "Un cliente con un plan vivo no se puede borrar. Completa el estado de la solicitud y el código del bloqueo.",
 "pista1": "La solicitud queda bloqueada. El código habla de un plan de pensión activo.",
 "pista2": "'BLOCKED' y 'PENSION_PLAN_ACTIVE'",
 "feature": r"""
  Scenario: Bloqueado
    * def p = call read('_plan.feature')
    * def karin = call read('_login.feature') usuarios.karin
    Given url erasureUrl
    And path '/api/v1/erasure-requests'
    And headers karin.auth
    And request { customerId: '#(p.customerId)', reason: 'Lo pide el cliente' }
    When method post
    Then status 201
    And match response.status == ⟦'BLOCKED'⟧
    And match response.blockers[0].code == ⟦'PENSION_PLAN_ACTIVE'⟧
    Given url customerUrl
    And path '/api/v1/internal/customers', p.customerId
    When method get
    Then match response.status == 'ACTIVE'
"""},
],
}

LECCIONES = [L28, L29, L30, L31, L32, L33]

def _triple(x):
    if isinstance(x, str): return x.replace("TRIPLE", '"' * 3)
    if isinstance(x, dict): return {k: _triple(v) for k, v in x.items()}
    if isinstance(x, list): return [_triple(v) for v in x]
    return x
LECCIONES = [_triple(l) for l in LECCIONES]
