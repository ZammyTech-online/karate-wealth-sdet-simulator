# Tramo 2 · Lecciones L16-L21 sobre las oleadas 1 y 2 (identidad, clientes, pagos, inversión y contratos).
# Necesitan el perfil nivel-6:  nordvik up -Profile nivel-6

LOGIN = r"""
# Ayudante compartido: pide un token OAuth2 con el flujo password.
# Uso:  * def erik = call read('_login.feature') usuarios.erik   →   erik.token
@ignore
Feature: Ayudante · pedir un token

  Scenario: Login
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = usuario
    And form field password = clave
    When method post
    Then status 200
    * def token = response.access_token
    * def auth = { Authorization: '#("Bearer " + token)' }
"""

CLIENTE_CON_DINERO = r"""
# Ayudante: crea un cliente nuevo de asesor.erik, le ingresa dinero de verdad (OCR + abono en el banco simulado)
# y le abre una cartera. Devuelve customerId, portfolioId, email y los tokens de erik y olof.
# Uso:  * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }
@ignore
Feature: Ayudante · cliente nuevo con dinero

  Scenario: Cliente con dinero
    # 'this:' busca el fichero junto a ESTE ayudante, no junto a la feature que lo llama
    * def erik = call read('this:_login.feature') usuarios.erik
    * def olof = call read('this:_login.feature') usuarios.olof

    # 1 · una persona sintética mayor de edad
    Given url personUrl
    And path '/api/v1/persons'
    And request { minAge: 25, maxAge: 60 }
    When method post
    Then status 200
    * def p = response.persons[0]

    # 2 · el asesor da de alta al cliente
    Given url customerUrl
    And path '/api/v1/customers'
    And headers erik.auth
    And request { firstName: '#(p.firstName)', lastName: '#(p.lastName)', nationalId: '#(p.nationalId)', email: '#(p.email)' }
    When method post
    Then status 201
    * def customerId = response.id
    * def email = p.email

    # 3 · operaciones abre un ingreso: Nordvik da una referencia OCR
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(customerId)', expectedAmount: '#(importe)' }
    When method post
    Then status 201
    * def deposito = response

    # 4 · el cliente transfiere desde su banco (simulador NordPay) con esa referencia
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def ibanCliente = response.electronic
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(ibanCliente)', debtorName: 'Cliente Academia', amount: '#(importe)', reference: '#(deposito.reference)' }
    When method post
    Then status 201

    # 5 · el aviso del banco llega por webhook: esperamos a que el ingreso quede RECEIVED
    * configure retry = { count: 40, interval: 500 }
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200

    # 6 · una cartera ISK 100 % en un fondo
    Given url portfolioUrl
    And path '/api/v1/portfolios'
    And headers erik.auth
    And request { customerId: '#(customerId)', name: '#("Cartera " + customerId.substring(0, 6))', type: 'ISK', targetAllocation: [{ isin: 'SE0000000003', percent: 100 }] }
    When method post
    Then status 201
    * def portfolioId = response.id
"""


IBAN = r"""
# Ayudante: un IBAN sueco válido y nuevo (la cuenta desde la que "transfiere" el cliente).
@ignore
Feature: Ayudante · IBAN de prueba

  Scenario: IBAN
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def iban = response.electronic
"""

# ───────────────────────────── L16 ─────────────────────────────
L16 = {
"code": "L16", "slug": "autenticacion-y-roles", "url": "iamUrl",
"readme": r"""
# L16 · Autenticación y roles: tokens OAuth2 en Karate

**Tramo 2.** Desde aquí trabajas con el Nordvik de verdad: clientes, asesores, contratos y dinero.
Arranca el perfil grande antes de empezar:

```
nordvik up -Profile nivel-6
```

## La idea

Casi todo en Nordvik exige un **token**. Lo pides a `iam-service` (puerto 6200) con el flujo *password* de OAuth2,
que se envía como formulario (`application/x-www-form-urlencoded`), no como JSON:

```gherkin
Given url iamUrl
And path '/api/v1/oauth/token'
And form field grant_type = 'password'
And form field username = 'asesor.erik'
And form field password = 'Erik2026!'
When method post
Then status 200
* def token = response.access_token
```

Luego lo mandas en cada petición con la cabecera `Authorization: Bearer <token>`:

```gherkin
And header Authorization = 'Bearer ' + token
```

## Los usuarios de prueba

| Usuario | Rol | Puede... |
|---|---|---|
| cliente.ana / cliente.lars | Cliente | ver lo suyo y nada más |
| asesor.erik / asesora.maja | Asesor | gestionar a *sus* clientes asignados |
| operaciones.olof | Operaciones | pagos, ingresos, procesos batch |
| compliance.karin | Cumplimiento | ver datos completos para auditar |
| admin | Administración | todo, incluido gestionar usuarios |

Todos están en `config/karate-config.js` como `usuarios.ana`, `usuarios.erik`... y hay un ayudante
`_login.feature` que hace el login por ti:

```gherkin
* def erik = call read('_login.feature') usuarios.erik
And headers erik.auth
```

## La norma

1. **401** significa "no sé quién eres" (falta el token o no vale). **403** significa "sé quién eres y no puedes".
2. Cada respuesta de error es un *problem* RFC 9457: mira siempre `code`, no el texto de `detail`.
3. Pide el token **una vez** por feature (`callonce`): pedirlo en cada escenario hace la suite lenta.

## El fundamento

Probar seguridad no es solo probar que entras: es probar que **cada rol ve exactamente lo que debe y ni un byte más**.
El caso feliz es el 20 % de tu trabajo; el otro 80 % son los 401 y 403.

## Tus ejercicios

`academia corregir L16`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L16. Ejecútalo con:  academia ejemplo L16
@ejemplo
Feature: L16 · Tokens y roles

  Scenario: Erik pide su token y lee a su clienta Ana
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = 'asesor.erik'
    And form field password = 'Erik2026!'
    When method post
    Then status 200
    And match response contains { access_token: '#string', token_type: 'Bearer', expires_in: '#number' }
    * def token = response.access_token

    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And header Authorization = 'Bearer ' + token
    When method get
    Then status 200
    And match response.id == ids.ana

  Scenario: Sin token, 401 con su código
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    When method get
    Then status 401
    And match response.code == 'AUTHENTICATION_REQUIRED'

  Scenario: Con el ayudante de login
    * def ana = call read('ejercicios/_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers ana.auth
    When method get
    Then status 200
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Tu primer token",
 "enunciado": "Pide un token como la clienta Ana (usuario cliente.ana, clave Ana2026!).",
 "pista1": "El usuario va en el campo username del formulario, entre comillas.",
 "pista2": "'cliente.ana'",
 "feature": r"""
  Scenario: Login de Ana
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = ⟦'cliente.ana'⟧
    And form field password = 'Ana2026!'
    When method post
    Then status 200
    And match response.access_token == '#string'
"""},
{"id": "E02", "title": "Una clave mala",
 "enunciado": "Con una contraseña incorrecta el login falla. Completa el estado esperado.",
 "pista1": "No es un 403: el servidor no sabe quién eres. Las credenciales inválidas en OAuth2 son un error del cliente.",
 "pista2": "400 (OAuth2 devuelve invalid_grant con 400). Míralo tú en Swagger: http://localhost:6200/swagger",
 "feature": r"""
  Scenario: Clave incorrecta
    Given url iamUrl
    And path '/api/v1/oauth/token'
    And form field grant_type = 'password'
    And form field username = 'cliente.ana'
    And form field password = 'no-es-esta'
    When method post
    Then status ⟦400⟧
"""},
{"id": "E03", "title": "Manda el token",
 "enunciado": "Ana ya tiene token. Completa la cabecera para leer su propia ficha de cliente.",
 "pista1": "El valor de la cabecera es la palabra Bearer, un espacio y el token.",
 "pista2": "'Bearer ' + ana.token",
 "feature": r"""
  Scenario: Ana lee su ficha
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And header Authorization = ⟦'Bearer ' + ana.token⟧
    When method get
    Then status 200
    And match response.id == ids.ana
"""},
{"id": "E04", "title": "401 sin token",
 "enunciado": "Sin cabecera Authorization, completa el estado y el código del problema.",
 "pista1": "Sin token el servidor no sabe quién eres. El código es AUTHENTICATION_REQUIRED.",
 "pista2": "401 y 'AUTHENTICATION_REQUIRED'",
 "feature": r"""
  Scenario: Anónimo
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    When method get
    Then status ⟦401⟧
    And match response.code == ⟦'AUTHENTICATION_REQUIRED'⟧
"""},
{"id": "E05", "title": "403: sé quién eres y no puedes",
 "enunciado": "Erik es asesor y no puede listar los usuarios del sistema. Completa estado y código.",
 "pista1": "Ahora sí hay token válido; lo que falta es el rol. El código habla de rol.",
 "pista2": "403 y 'ROLE_NOT_ALLOWED'",
 "feature": r"""
  Scenario: Erik no administra usuarios
    * def erik = call read('_login.feature') usuarios.erik
    Given url iamUrl
    And path '/api/v1/users'
    And headers erik.auth
    When method get
    Then status ⟦403⟧
    And match response.code == ⟦'ROLE_NOT_ALLOWED'⟧
"""},
{"id": "E06", "title": "Un solo login por feature",
 "enunciado": "Pide el token una sola vez para todos los escenarios: completa la palabra que lo hace.",
 "pista1": "Es como call, pero se ejecuta una vez por feature.",
 "pista2": "callonce",
 "feature": r"""
  Background:
    * def admin = ⟦callonce⟧ read('_login.feature') usuarios.admin

  Scenario: El admin lista usuarios
    Given url iamUrl
    And path '/api/v1/users'
    And headers admin.auth
    When method get
    Then status 200

  Scenario: Y sigue teniendo el mismo token
    * match admin.token == '#string'
"""},
],
}

# ───────────────────────────── L17 ─────────────────────────────
L17 = {
"code": "L17", "slug": "idor-y-mascaras", "url": "customerUrl",
"readme": r"""
# L17 · IDOR y máscaras: que cada uno vea solo lo suyo

## La idea

Un **IDOR** (*Insecure Direct Object Reference*) es el fallo más común de una API: cambias el id de la URL
y ves los datos de otra persona. En Nordvik lo probamos así:

- Ana pide la ficha de **Lars** → **404 CUSTOMER_NOT_FOUND**. No es un 403: Nordvik ni siquiera confirma que Lars exista.
- Erik pide a **Sofía**, que no es clienta suya → **403 ACCESS_DENIED** con el motivo en `detail`. A un empleado sí se le explica.

Y cuando sí puedes ver al cliente, puede que no lo veas entero. El número nacional (`nationalId`) sale:

| Quién mira | Cómo sale |
|---|---|
| El propio cliente | completo |
| Su asesor | enmascarado: `19850312-****` → contiene `**-****` |
| Cumplimiento | completo (está auditando) |

## La norma

1. Prueba cada recurso con **al menos tres actores**: el dueño, otro del mismo rol y alguien de otro rol.
2. Un 404 por IDOR es correcto si el actor no debe saber que el recurso existe.
3. Las máscaras se comprueban con `contains` y `!contains`, nunca comparando el valor entero (cambia en cada alta).

## El fundamento

La tabla *actor × recurso × resultado* es tu mapa de seguridad. `Scenario Outline` la convierte en pruebas
de una sola pasada: cada fila es un actor.

## Tus ejercicios

`academia corregir L17`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L17. Ejecútalo con:  academia ejemplo L17
@ejemplo
Feature: L17 · IDOR y máscaras

  Scenario Outline: <quien> lee a Ana → <estado>
    * def actor = call read('ejercicios/_login.feature') usuarios.<quien>
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers actor.auth
    When method get
    Then status <estado>

    Examples:
      | quien | estado |
      | ana   | 200    |
      | erik  | 200    |
      | karin | 200    |
      | lars  | 404    |

  Scenario: El asesor ve el número nacional enmascarado
    * def erik = call read('ejercicios/_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers erik.auth
    When method get
    Then status 200
    And match response.nationalId contains '**-****'
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Ana husmea a Lars",
 "enunciado": "Ana pide la ficha de Lars. Completa el estado y el código que protegen a Lars.",
 "pista1": "Nordvik no confirma que Lars exista para Ana: responde como si no existiera.",
 "pista2": "404 y 'CUSTOMER_NOT_FOUND'",
 "feature": r"""
  Scenario: IDOR entre clientes
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers', ⟦ids.lars⟧
    And headers ana.auth
    When method get
    Then status ⟦404⟧
    And match response.code == ⟦'CUSTOMER_NOT_FOUND'⟧
"""},
{"id": "E02", "title": "Asesor sin asignación",
 "enunciado": "Erik pide a Sofía, que no es clienta suya. Completa el estado y la frase que explica el motivo.",
 "pista1": "Un empleado sí recibe el motivo. Busca en detail el texto 'no está asignado'.",
 "pista2": "403 y 'no está asignado'",
 "feature": r"""
  Scenario: Erik y Sofía
    * def erik = call read('_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.sofia
    And headers erik.auth
    When method get
    Then status ⟦403⟧
    And match response.code == 'ACCESS_DENIED'
    And match response.detail contains ⟦'no está asignado'⟧
"""},
{"id": "E03", "title": "La máscara del asesor",
 "enunciado": "Erik lee a Ana: comprueba que el número nacional sale enmascarado.",
 "pista1": "Usa contains con el trozo de máscara: dos asteriscos, guion y cuatro asteriscos.",
 "pista2": "contains '**-****'",
 "feature": r"""
  Scenario: Máscara
    * def erik = call read('_login.feature') usuarios.erik
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers erik.auth
    When method get
    Then status 200
    And match response.nationalId ⟦contains '**-****'⟧
"""},
{"id": "E04", "title": "Cumplimiento lo ve todo",
 "enunciado": "Karin (cumplimiento) lee a Ana: el número nacional no lleva ni un asterisco.",
 "pista1": "Lo contrario de contains en Karate se escribe con una exclamación delante.",
 "pista2": "!contains '*'",
 "feature": r"""
  Scenario: Sin máscara
    * def karin = call read('_login.feature') usuarios.karin
    Given url customerUrl
    And path '/api/v1/customers', ids.ana
    And headers karin.auth
    When method get
    Then status 200
    And match response.nationalId ⟦!contains⟧ '*'
"""},
{"id": "E05", "title": "La tabla de seguridad",
 "enunciado": "Completa la tabla: qué estado recibe cada actor al leer a Lars.",
 "pista1": "Lars se ve a sí mismo. Ana no debe ni saber que existe. Admin lo ve todo.",
 "pista2": "lars 200 · ana 404 · admin 200",
 "feature": r"""
  Scenario Outline: <quien> lee a Lars
    * def actor = call read('_login.feature') usuarios.<quien>
    Given url customerUrl
    And path '/api/v1/customers', ids.lars
    And headers actor.auth
    When method get
    Then status <estado>

    Examples:
      | quien | estado |
      | lars  | ⟦200⟧ |
      | ana   | ⟦404⟧ |
      | admin | ⟦200⟧ |
"""},
{"id": "E06", "title": "El cliente no da altas",
 "enunciado": "Ana intenta dar de alta un cliente. Su token no tiene el permiso (scope) necesario: completa el código.",
 "pista1": "No falla por rol sino porque al token le falta un scope. El código lo dice tal cual.",
 "pista2": "'SCOPE_MISSING'",
 "feature": r"""
  Scenario: Alta sin permiso
    * def ana = call read('_login.feature') usuarios.ana
    Given url customerUrl
    And path '/api/v1/customers'
    And headers ana.auth
    And request { firstName: 'Ana', lastName: 'Clon', nationalId: '19900101-1234', email: 'clon@example.com' }
    When method post
    Then status 403
    And match response.code == ⟦'SCOPE_MISSING'⟧
"""},
],
}

# ───────────────────────────── L18 ─────────────────────────────
L18 = {
"code": "L18", "slug": "consistencia-eventual", "url": "paymentsUrl",
"readme": r"""
# L18 · Consistencia eventual: esperar sin dormir

## La idea

En Nordvik muchas cosas **no pasan en la misma petición**. Cuando un cliente transfiere dinero:

```
cliente ──transferencia──▶ NordPay (banco simulado, 7706)
                               │ webhook camt.054
                               ▼
                        nordpay-adapter (7502) ──evento──▶ event-bus ──▶ payments-gateway (7000)
                                                                          │ ingreso RECEIVED
                                                                          ▼
                                                                   ledger · cash-management
```

Tu petición al banco responde **201 en milisegundos**, pero el ingreso tarda un poco en aparecer como `RECEIVED`.
Si compruebas justo después, tu test falla *a veces*: es un **test inestable** (*flaky*).

## La herramienta: `retry until`

```gherkin
* configure retry = { count: 30, interval: 500 }
Given url paymentsUrl
And path '/api/v1/payments', depositoId
And headers olof.auth
And retry until response.status == 'RECEIVED'
When method get
Then status 200
```

Karate repite la petición hasta que la condición se cumple (o se agotan los intentos y falla con un mensaje claro).

## La norma

1. **Nunca** `Thread.sleep` ni `karate.pause` fijos: o esperas de más (suite lenta) o de menos (suite inestable).
2. `retry until` va **antes** de `method`, y la condición es JavaScript sobre `response` y `responseStatus`.
3. Pon un techo razonable: `count × interval` debe ser el doble del tiempo normal, no diez veces más.
4. Si la condición puede llegar a un estado final malo (`REJECTED`), inclúyelo: `response.status == 'RECEIVED' || response.status == 'REJECTED'`, y luego comprueba cuál fue.

## El fundamento

El tiempo de espera es parte del contrato del servicio. Si un ingreso tarda 20 s en vez de 1 s, `retry until`
lo descubre: tu techo es tu SLA.

## Tus ejercicios

Usan el ayudante `_cliente-con-dinero.feature`, que hace todo el viaje de un ingreso (léelo: es la mejor documentación del flujo).
`academia corregir L18`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L18. Ejecútalo con:  academia ejemplo L18
@ejemplo
Feature: L18 · Un ingreso de principio a fin

  Background:
    * def olof = callonce read('ejercicios/_login.feature') usuarios.olof
    * configure retry = { count: 40, interval: 500 }

  Scenario: Abrir el ingreso, transferir y esperar a que llegue
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '250.00' }
    When method post
    Then status 201
    And match response contains { status: 'AWAITING_FUNDS', reference: '#string' }
    * def deposito = response

    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def iban = response.electronic

    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(iban)', debtorName: 'Ana', amount: '250.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201

    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200
    And match response.amount == '250.00'
""",
"apoyo": {"_login.feature": LOGIN, "_iban.feature": IBAN, "_cliente-con-dinero.feature": CLIENTE_CON_DINERO},
"ejercicios": [
{"id": "E01", "title": "Cuántas veces reintentar",
 "enunciado": "Configura 40 intentos cada medio segundo (20 s de techo).",
 "pista1": "count es el número de intentos, interval los milisegundos entre ellos.",
 "pista2": "count: 40, interval: 500",
 "feature": r"""
  Scenario: Configurar el reintento
    * configure retry = { count: ⟦40⟧, interval: ⟦500⟧ }
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '100.00' }
    When method post
    Then status 201
    * def deposito = response
    * def banco = call read('_iban.feature')
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(banco.iban)', debtorName: 'Ana', amount: '100.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200
"""},
{"id": "E02", "title": "La condición de espera",
 "enunciado": "Escribe la condición: espera hasta que el ingreso esté RECEIVED.",
 "pista1": "Es JavaScript sobre response. Compara el campo status con el texto del estado final.",
 "pista2": "response.status == 'RECEIVED'",
 "feature": r"""
  Scenario: Esperar al ingreso
    * configure retry = { count: 40, interval: 500 }
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '120.00' }
    When method post
    Then status 201
    * def deposito = response
    * def banco = call read('_iban.feature')
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(banco.iban)', debtorName: 'Ana', amount: '120.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until ⟦response.status == 'RECEIVED'⟧
    When method get
    Then status 200
    And match response.amount == '120.00'
"""},
{"id": "E03", "title": "Recién creado, todavía no",
 "enunciado": "Justo al abrir el ingreso, antes de transferir nada, ¿en qué estado está?",
 "pista1": "Nordvik está esperando a que llegue el dinero. Mira el ejemplo de la lección.",
 "pista2": "'AWAITING_FUNDS'",
 "feature": r"""
  Scenario: Estado inicial
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '75.00' }
    When method post
    Then status 201
    And match response.status == ⟦'AWAITING_FUNDS'⟧
    And match response.reference == '#regex [0-9]+'
"""},
{"id": "E04", "title": "El dinero llega a la caja del cliente",
 "enunciado": "Tras el viaje completo, el efectivo disponible del cliente es el importe ingresado.\nCompleta la llamada al ayudante y la espera sobre available.",
 "pista1": "El ayudante recibe un objeto con importe. El saldo también llega de forma eventual: espera sobre response.available.",
 "pista2": "{ importe: '800.00' } y response.available == '800.00'",
 "feature": r"""
  Scenario: Caja disponible
    * def cliente = call read('_cliente-con-dinero.feature') ⟦{ importe: '800.00' }⟧
    * configure retry = { count: 40, interval: 500 }
    Given url cashUrl
    And path '/api/v1/internal/customers', cliente.customerId, 'cash'
    And retry until ⟦response.available == '800.00'⟧
    When method get
    Then status 200
"""},
{"id": "E05", "title": "Un estado final malo también es final",
 "enunciado": "Una condición robusta acepta los dos finales posibles. Completa el operador 'o' de JavaScript.",
 "pista1": "En JavaScript, 'o' se escribe con dos barras verticales.",
 "pista2": "||",
 "feature": r"""
  Scenario: Esperar a cualquier final
    * configure retry = { count: 40, interval: 500 }
    * def olof = call read('_login.feature') usuarios.olof
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(ids.ana)', expectedAmount: '60.00' }
    When method post
    Then status 201
    * def deposito = response
    * def banco = call read('_iban.feature')
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(banco.iban)', debtorName: 'Ana', amount: '60.00', reference: '#(deposito.reference)' }
    When method post
    Then status 201
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED' ⟦||⟧ response.status == 'REJECTED'
    When method get
    Then status 200
    And match response.status == 'RECEIVED'
"""},
],
}

# ───────────────────────────── L19 ─────────────────────────────
L19 = {
"code": "L19", "slug": "idempotencia", "url": "ordersUrl",
"readme": r"""
# L19 · Idempotencia: la misma orden dos veces no compra dos veces

## La idea

La red falla. El móvil del cliente manda "comprar 300 SEK", no recibe respuesta y **lo vuelve a mandar**.
Si Nordvik compra dos veces, el cliente pierde dinero. La solución es la cabecera **`Idempotency-Key`**:

```gherkin
* def clave = 'k-' + java.util.UUID.randomUUID()
Given url ordersUrl
And path '/api/v1/orders'
And headers erik.auth
And header Idempotency-Key = clave
And request { portfolioId: '#(cartera)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
When method post
Then status 201
```

| Envías... | Nordvik responde |
|---|---|
| Sin clave | 400 `IDEMPOTENCY_KEY_REQUIRED` |
| Clave corta o con símbolos raros | 400 `IDEMPOTENCY_KEY_INVALID` (8-64 caracteres: letras, números, `-`, `_`) |
| Misma clave, misma petición | **la misma respuesta**, sin repetir nada, con la cabecera `Idempotent-Replayed: true` |
| Misma clave, otra petición | 422 `IDEMPOTENCY_KEY_REUSED` |

Ojo: **los errores también se guardan**. Si la primera vez la orden falló por falta de fondos, repetirla con la misma clave
devuelve el mismo 422 aunque ahora haya fondos. Para reintentar de verdad, clave nueva.

## Leer una cabecera de la respuesta

```gherkin
And match karate.response.header('idempotent-replayed') == 'true'
```

`karate.response.header()` no distingue mayúsculas de minúsculas, igual que HTTP.

## La norma

1. Una clave nueva **por operación de negocio**, no por intento.
2. Prueba siempre los cuatro casos de la tabla; el de "misma clave, otra petición" es el que más fallos encuentra.
3. Comprueba el **efecto**, no solo la respuesta: tras dos envíos, el dinero disponible ha bajado una sola vez.

## Tus ejercicios

`academia corregir L19` (cada ejercicio crea su propio cliente con dinero: tarda unos segundos).
""",
"ejemplo": r"""
# Ejemplo resuelto de L19. Ejecútalo con:  academia ejemplo L19
@ejemplo
Feature: L19 · Idempotencia de órdenes

  Background:
    * def cliente = callonce read('ejercicios/_cliente-con-dinero.feature') { importe: '1000.00' }
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }

  Scenario: Dos envíos, una sola compra
    * def clave = 'k-' + java.util.UUID.randomUUID()
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 201
    * def primera = response

    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 201
    And match response.id == primera.id
    And match karate.response.header('idempotent-replayed') == 'true'

    * configure retry = { count: 20, interval: 500 }
    Given url cashUrl
    And path '/api/v1/internal/customers', cliente.customerId, 'cash'
    And retry until response.available == '700.00'
    When method get
    Then status 200
""",
"apoyo": {"_login.feature": LOGIN, "_cliente-con-dinero.feature": CLIENTE_CON_DINERO},
"ejercicios": [
{"id": "E01", "title": "Sin clave no hay orden",
 "enunciado": "Manda una orden sin Idempotency-Key. Completa el estado y el código.",
 "pista1": "Es un error de la petición (falta algo), no de negocio. El código dice que la clave es obligatoria.",
 "pista2": "400 y 'IDEMPOTENCY_KEY_REQUIRED'",
 "feature": r"""
  Scenario: Falta la clave
    * def erik = call read('_login.feature') usuarios.erik
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers erik.auth
    And request { portfolioId: '#(ids.carteraAna)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status ⟦400⟧
    And match response.code == ⟦'IDEMPOTENCY_KEY_REQUIRED'⟧
"""},
{"id": "E02", "title": "Una clave demasiado corta",
 "enunciado": "La clave 'corta' tiene 5 caracteres. Completa el código del error.",
 "pista1": "La clave existe pero no tiene el formato válido.",
 "pista2": "'IDEMPOTENCY_KEY_INVALID'",
 "feature": r"""
  Scenario: Clave inválida
    * def erik = call read('_login.feature') usuarios.erik
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers erik.auth
    And header Idempotency-Key = 'corta'
    And request { portfolioId: '#(ids.carteraAna)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status 400
    And match response.code == ⟦'IDEMPOTENCY_KEY_INVALID'⟧
"""},
{"id": "E03", "title": "La repetición devuelve lo mismo",
 "enunciado": "Manda la misma orden dos veces con la misma clave. Completa la cabecera y la comparación de ids.",
 "pista1": "La cabecera se llama Idempotency-Key. La segunda respuesta tiene el mismo id que la primera.",
 "pista2": "Idempotency-Key y primera.id",
 "feature": r"""
  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Repetición
    * def clave = 'k-' + java.util.UUID.randomUUID()
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header ⟦Idempotency-Key⟧ = clave
    And request orden
    When method post
    Then status 201
    * def primera = response
    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 201
    And match response.id == ⟦primera.id⟧
    And match karate.response.header('idempotent-replayed') == 'true'
"""},
{"id": "E04", "title": "Misma clave, otra orden",
 "enunciado": "Reutiliza la clave con otro importe. Completa el estado y el código.",
 "pista1": "La petición está bien formada pero viola una regla: la clave ya se usó para otra cosa. Es un 422.",
 "pista2": "422 y 'IDEMPOTENCY_KEY_REUSED'",
 "feature": r"""
  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Clave reutilizada
    * def clave = 'k-' + java.util.UUID.randomUUID()
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
    When method post
    Then status 201
    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '301.00' }
    When method post
    Then status ⟦422⟧
    And match response.code == ⟦'IDEMPOTENCY_KEY_REUSED'⟧
"""},
{"id": "E05", "title": "Los errores también se repiten",
 "enunciado": "Una orden sin fondos falla. Repetida con la misma clave devuelve el mismo error, marcado como repetición.\nCompleta el código y el valor de la cabecera.",
 "pista1": "El cliente tiene 1000 SEK y pide 5000: faltan fondos. La cabecera de repetición vale el texto true.",
 "pista2": "'INSUFFICIENT_FUNDS' y 'true'",
 "feature": r"""
  Background:
    * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }

  Scenario: Error repetido
    * def clave = 'k-' + java.util.UUID.randomUUID()
    * def orden = { portfolioId: '#(cliente.portfolioId)', isin: 'SE0000000001', side: 'BUY', amount: '5000.00' }
    Given url ordersUrl
    And path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 422
    And match response.code == ⟦'INSUFFICIENT_FUNDS'⟧
    Given path '/api/v1/orders'
    And headers cliente.erik.auth
    And header Idempotency-Key = clave
    And request orden
    When method post
    Then status 422
    And match karate.response.header('idempotent-replayed') == ⟦'true'⟧
"""},
],
}

# ───────────────────────────── L20 ─────────────────────────────
L20 = {
"code": "L20", "slug": "contratos-y-firma", "url": "contractsUrl",
"readme": r"""
# L20 · Contratos y firma electrónica: un flujo con estados

## La idea

Un contrato de Nordvik vive en una **máquina de estados**:

```
DRAFT ──send──▶ PENDING_SIGNATURE ──firma──▶ (PARTIALLY_SIGNED) ──▶ ACTIVE ──▶ WITHDRAWN · SUPERSEDED · TERMINATED
                                     └──▶ EXPIRED · INVALID_SIGNATURE
```

1. El asesor lo crea (`POST /api/v2/contracts`) → **DRAFT**. El contenido sale de la plantilla y del motor de cláusulas.
2. El asesor lo envía (`POST /{id}/send`) → **PENDING_SIGNATURE**. Ya no se puede tocar.
3. El cliente abre una **sesión de firma** (`POST /{id}/signing-sessions` con `party: 'CUSTOMER'`). Nordvik pide la firma a NordID
   (el BankID simulado, puerto 7701), que tarda unos segundos: la sesión empieza en `PENDING`.
4. Cuando NordID termina, la sesión pasa a `COMPLETED` y el contrato a **ACTIVE**.

## La integridad: qué se firmó

Cada contrato tiene un `contentHash` (SHA-256 de su contenido canónico). La sesión de firma guarda `signedHash`:
**el cliente firma exactamente ese hash**. Si alguien cambiara una cláusula después, el hash no coincidiría.

## Una trampa real: una firma a la vez

Como el BankID de verdad, NordID solo permite **una firma en curso por persona**. Si abres otra sesión mientras la anterior
sigue `PENDING`, recibes **409 `SIGNING_ALREADY_IN_PROGRESS`**. Por eso cada prueba que abre una sesión **espera a que termine**:
si no, rompe a la siguiente (y ese test "falla a veces" según el orden en que se ejecuten).

## La norma

1. Prueba **cada transición** y también las prohibidas (enviar dos veces, firmar un DRAFT).
2. Las transiciones asíncronas (la firma) se esperan con `retry until`, como en L18.
3. Guarda lo que necesitas de cada paso en variables con nombre de negocio: `contrato`, `sesion`.

## Tus ejercicios

`academia corregir L20`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L20. Ejecútalo con:  academia ejemplo L20
@ejemplo
Feature: L20 · Del borrador al contrato activo

  Background:
    * def erik = callonce read('ejercicios/_login.feature') usuarios.erik
    * def ana = callonce read('ejercicios/_login.feature') usuarios.ana
    * configure retry = { count: 30, interval: 1000 }
    * url contractsUrl

  Scenario: Ana firma su contrato marco
    Given path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And match response.status == 'DRAFT'
    * def contrato = response

    Given path '/api/v2/contracts', contrato.id, 'send'
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    And match response.status == 'PENDING_SIGNATURE'

    Given path '/api/v2/contracts', contrato.id, 'signing-sessions'
    And headers ana.auth
    And request { party: 'CUSTOMER' }
    When method post
    Then status 201
    And match response.signedHash == contrato.contentHash
    * def sesion = response

    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until response.status != 'PENDING'
    When method get
    Then status 200
    And match response.status == 'COMPLETED'

    Given path '/api/v2/contracts', contrato.id
    And headers ana.auth
    And retry until response.status == 'ACTIVE'
    When method get
    Then status 200
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Nace un borrador",
 "enunciado": "Erik crea un contrato marco para Ana. Completa el tipo y el estado inicial.",
 "pista1": "El contrato marco es MASTER_AGREEMENT. Todo contrato nace como borrador.",
 "pista2": "'MASTER_AGREEMENT' y 'DRAFT'",
 "feature": r"""
  Scenario: Crear
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: ⟦'MASTER_AGREEMENT'⟧, customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And match response.status == ⟦'DRAFT'⟧
    And match response.contentHash == '#regex [0-9a-f]{64}'
"""},
{"id": "E02", "title": "Enviar a firma",
 "enunciado": "Envía el borrador al cliente. Completa el último trozo de la ruta y el nuevo estado.",
 "pista1": "La acción de enviar es una subruta del contrato. Después espera la firma.",
 "pista2": "'send' y 'PENDING_SIGNATURE'",
 "feature": r"""
  Scenario: Enviar
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id, ⟦'send'⟧
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    And match response.status == ⟦'PENDING_SIGNATURE'⟧
"""},
{"id": "E03", "title": "Lo que se firma es el hash",
 "enunciado": "Ana abre la sesión de firma. Comprueba que firma exactamente el contenido del contrato.",
 "pista1": "La parte que firma es el cliente. El signedHash de la sesión es el contentHash del contrato.",
 "pista2": "'CUSTOMER' y contrato.contentHash",
 "feature": r"""
  Scenario: Hash firmado
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id, 'send'
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions'
    And headers ana.auth
    And request { party: ⟦'CUSTOMER'⟧ }
    When method post
    Then status 201
    And match response.status == 'PENDING'
    And match response.signedHash == ⟦contrato.contentHash⟧
    # NordID solo admite una firma en curso por persona: esperamos a que acabe para no bloquear a la siguiente prueba
    * def sesion = response
    * configure retry = { count: 30, interval: 1000 }
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until response.status != 'PENDING'
    When method get
    Then status 200
"""},
{"id": "E04", "title": "Esperar a NordID",
 "enunciado": "La firma tarda unos segundos. Escribe la condición: espera mientras siga PENDING.",
 "pista1": "Quieres repetir mientras el estado sea PENDING, es decir, hasta que sea distinto de PENDING.",
 "pista2": "response.status != 'PENDING'",
 "feature": r"""
  Scenario: Firma completa
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    * configure retry = { count: 30, interval: 1000 }
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id, 'send'
    And headers erik.auth
    And request {}
    When method post
    Then status 200
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions'
    And headers ana.auth
    And request { party: 'CUSTOMER' }
    When method post
    Then status 201
    * def sesion = response
    Given path '/api/v2/contracts', contrato.id, 'signing-sessions', sesion.id
    And headers ana.auth
    And retry until ⟦response.status != 'PENDING'⟧
    When method get
    Then status 200
    And match response.status == 'COMPLETED'
"""},
{"id": "E05", "title": "Un contrato ajeno",
 "enunciado": "Lars intenta leer el contrato de Ana. Completa el estado y el código (recuerda L17).",
 "pista1": "Es un IDOR entre clientes: Nordvik no confirma que el contrato exista.",
 "pista2": "404 y 'CONTRACT_NOT_FOUND'",
 "feature": r"""
  Scenario: IDOR de contratos
    * def erik = call read('_login.feature') usuarios.erik
    * def lars = call read('_login.feature') usuarios.lars
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def contrato = response
    Given path '/api/v2/contracts', contrato.id
    And headers lars.auth
    When method get
    Then status ⟦404⟧
    And match response.code == ⟦'CONTRACT_NOT_FOUND'⟧
"""},
{"id": "E06", "title": "Las cláusulas vienen ordenadas",
 "enunciado": "Comprueba que todas las cláusulas tienen número, código y texto, y que la primera es la GEN-01.",
 "pista1": "match each recorre la lista. La primera cláusula es la posición 0.",
 "pista2": "match each y content.clauses[0].code",
 "feature": r"""
  Scenario: Cláusulas
    * def erik = call read('_login.feature') usuarios.erik
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    And ⟦match each⟧ response.content.clauses == { clauseNo: '#number', code: '#string', title: '#string', text: '#string' }
    And match response.⟦content.clauses[0].code⟧ == 'GEN-01'
"""},
],
}

# ───────────────────────────── L21 ─────────────────────────────
L21 = {
"code": "L21", "slug": "composicion-de-apis", "url": "compositionUrl",
"readme": r"""
# L21 · Dos APIs, un JSON: probar una composición

## La idea

`composition-service` (6107) **une respuestas de varios servicios** en un solo JSON. La composición C1,
*contract-bundle*, junta el contrato (`contract-service`) con su anexo (`document-vault`):

```json
{
  "status": "COMPLETE",
  "bundle": { "contract": { ... }, "annex": { ... } },
  "sources": [ { "service": "contract-service", "version": 2, "status": 200, "ms": 6, "fetchedAt": "..." }, { "service": "document-vault", ... } ],
  "missing": []
}
```

Si falta una parte (el contrato no tiene anexo), la composición **no falla**: responde `PARTIAL` y lo dice en `missing`.

## Qué se prueba de una composición

1. **Que es la unión exacta**: `bundle.contract` es *igual* a lo que devuelve el servicio de contratos. Con `match ==` sobre
   objetos enteros, Karate compara campo a campo y te dice exactamente cuál difiere.
2. **Que es honesta cuando falta algo**: `PARTIAL` + `missing`.
3. **Que respeta la seguridad de cada fuente**: Lars no ve el bundle de Ana (404), y sin token es 401.
4. **La trazabilidad**: con la cabecera `X-Composition-Trace: true` añade un bloque `trace`; sin ella no aparece.

## La norma

1. Compara la composición contra **las fuentes reales**, llamadas en el mismo test, nunca contra un JSON copiado.
2. `'#notpresent'` comprueba que un campo **no existe**; `'#present'` que existe aunque sea null.

## Tus ejercicios

`academia corregir L21`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L21. Ejecútalo con:  academia ejemplo L21
@ejemplo
Feature: L21 · contract-bundle

  Background:
    * def erik = callonce read('ejercicios/_login.feature') usuarios.erik
    * def ana = callonce read('ejercicios/_login.feature') usuarios.ana

  Scenario: Contrato + anexo = bundle completo
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'ADVISORY_MANDATE', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id

    Given path '/api/v2/contracts', id, 'annexes'
    And headers erik.auth
    And request { kind: 'FEE_SCHEDULE', content: { managementFee: '0.45', entryFee: '0.00', currency: 'SEK' } }
    When method post
    Then status 201

    Given path '/api/v2/contracts', id
    And headers ana.auth
    When method get
    Then status 200
    * def contrato = response

    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == 'COMPLETE'
    And match response.bundle.contract == contrato
    And match response.bundle.annex.contractId == id
    And match response.sources[*].service == ['contract-service', 'document-vault']
""",
"apoyo": {"_login.feature": LOGIN},
"ejercicios": [
{"id": "E01", "title": "Sin anexo, parcial",
 "enunciado": "Un contrato sin anexo da una composición parcial. Completa el estado y lo que falta.",
 "pista1": "La composición no falla: avisa. missing es una lista con el nombre de la parte ausente.",
 "pista2": "'PARTIAL' y ['annex']",
 "feature": r"""
  Scenario: Parcial
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == ⟦'PARTIAL'⟧
    And match response.missing == ⟦['annex']⟧
"""},
{"id": "E02", "title": "La unión exacta",
 "enunciado": "Con anexo, el bundle contiene el contrato tal cual. Completa la comparación.",
 "pista1": "Ya leíste el contrato del servicio de contratos y lo guardaste en una variable. Compáralo entero.",
 "pista2": "contrato",
 "feature": r"""
  Scenario: Unión exacta
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'ADVISORY_MANDATE', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/contracts', id, 'annexes'
    And headers erik.auth
    And request { kind: 'FEE_SCHEDULE', content: { managementFee: '0.45', entryFee: '0.00', currency: 'SEK' } }
    When method post
    Then status 201
    Given path '/api/v2/contracts', id
    And headers ana.auth
    When method get
    Then status 200
    * def contrato = response
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.status == 'COMPLETE'
    And match response.bundle.contract == ⟦contrato⟧
"""},
{"id": "E03", "title": "Las fuentes",
 "enunciado": "Comprueba la lista de servicios de los que sale el bundle, en orden.",
 "pista1": "[*] saca un campo de todos los elementos de una lista. Primero contratos, luego la bóveda de documentos.",
 "pista2": "['contract-service', 'document-vault']",
 "feature": r"""
  Scenario: Fuentes
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'ADVISORY_MANDATE', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given path '/api/v2/contracts', id, 'annexes'
    And headers erik.auth
    And request { kind: 'FEE_SCHEDULE', content: { managementFee: '0.45', entryFee: '0.00', currency: 'SEK' } }
    When method post
    Then status 201
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.sources[*].service == ⟦['contract-service', 'document-vault']⟧
    # contains: cada fuente trae además fetchedAt y ms, que cambian en cada llamada
    And match each response.sources contains { service: '#string', version: '#number', status: 200 }
"""},
{"id": "E04", "title": "La traza, solo si la pides",
 "enunciado": "Pide la composición con traza y sin ella. Completa la cabecera y el marcador de 'no existe'.",
 "pista1": "La cabecera se llama X-Composition-Trace. El marcador de Karate para un campo ausente empieza por #not.",
 "pista2": "X-Composition-Trace y '#notpresent'",
 "feature": r"""
  Scenario: Traza
    * def erik = call read('_login.feature') usuarios.erik
    * def ana = call read('_login.feature') usuarios.ana
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    And header ⟦X-Composition-Trace⟧ = 'true'
    When method get
    Then status 200
    And match response.trace == '#object'
    Given path '/api/v2/compositions/contract-bundle', id
    And headers ana.auth
    When method get
    Then status 200
    And match response.trace == ⟦'#notpresent'⟧
"""},
{"id": "E05", "title": "La composición respeta la seguridad",
 "enunciado": "Lars pide el bundle de Ana y alguien lo pide sin token. Completa los dos estados.",
 "pista1": "Lars es un IDOR (como en L17). Sin token, el servidor no sabe quién eres.",
 "pista2": "404 y 401",
 "feature": r"""
  Scenario: Seguridad heredada
    * def erik = call read('_login.feature') usuarios.erik
    * def lars = call read('_login.feature') usuarios.lars
    Given url contractsUrl
    And path '/api/v2/contracts'
    And headers erik.auth
    And request { type: 'MASTER_AGREEMENT', customerId: '#(ids.ana)', accountType: 'ISK' }
    When method post
    Then status 201
    * def id = response.id
    Given url compositionUrl
    And path '/api/v2/compositions/contract-bundle', id
    And headers lars.auth
    When method get
    Then status ⟦404⟧
    Given path '/api/v2/compositions/contract-bundle', id
    When method get
    Then status ⟦401⟧
    And match response.code == 'AUTHENTICATION_REQUIRED'
"""},
],
}

LECCIONES = [L16, L17, L18, L19, L20, L21]
