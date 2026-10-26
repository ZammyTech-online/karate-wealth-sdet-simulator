# Lecciones L10-L15.

L10 = {
"code": "L10", "slug": "scenario-outline", "url": "calendarUrl",
"readme": r"""
# L10 · Muchos casos con una sola prueba: Scenario Outline

**En esta lección** escribes una prueba una vez y la ejecutas con muchos datos distintos.

## La idea

Cuando la misma comprobación se repite con distintos valores, no copies el escenario: usa `Scenario Outline` y una tabla `Examples`. Cada fila es un caso; cada columna, un valor que se sustituye en `<nombre>`.

```gherkin
Scenario Outline: ¿Es hábil el <fecha>?
  Given url calendarUrl
  And path '/api/v1/calendar/XSTO/is-trading-day'
  And param date = '<fecha>'
  When method get
  Then status 200
  And match response.tradingDay == <habil>

  Examples:
    | fecha      | habil |
    | 2026-10-26 | true  |
    | 2026-12-25 | false |
```

La tabla también puede venir de un fichero: `Examples: | read('festivos.csv') |`.

## La norma

1. `<columna>` se sustituye **como texto** antes de ejecutar: si quieres un texto, ponle comillas (`'<fecha>'`); si quieres un número o booleano, sin comillas (`<habil>`).
2. Cada fila es un escenario independiente en el reporte: si falla una, se ve cuál.
3. Los datos de prueba grandes van en CSV o JSON, no en el `.feature`.

## El fundamento

Las reglas de negocio se prueban en sus **bordes**: el último día hábil del año, el festivo que cae en viernes, el día después de Navidad. Una tabla hace visible qué bordes has cubierto y cuáles faltan.

## Tus ejercicios

`academia corregir L10`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L10. Ejecútalo con:  academia ejemplo L10
@ejemplo
Feature: L10 · Una prueba, muchos casos

  Scenario Outline: El <fecha> en <mercado> es hábil: <habil>
    Given url calendarUrl
    And path '/api/v1/calendar', '<mercado>', 'is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.tradingDay == <habil>

    Examples:
      | mercado | fecha      | habil |
      | XSTO    | 2026-10-26 | true  |
      | XSTO    | 2026-06-06 | false |
      | XOSL    | 2026-06-06 | false |
      | XOSL    | 2026-05-18 | true  |
      | XCSE    | 2026-06-05 | false |
""",
"apoyo": {
"festivos.csv": "fecha,nombre\n2026-01-01,Año Nuevo\n2026-01-06,Epifanía\n2026-04-03,Viernes Santo\n2026-05-14,Ascensión\n2026-06-19,Víspera de San Juan\n2026-12-24,Nochebuena\n",
},
"ejercicios": [
{"id": "E01", "title": "Rellena la tabla",
 "enunciado": "Completa el valor esperado de las dos últimas filas: Navidad y un sábado.",
 "pista1": "¿Abre la bolsa en Navidad? ¿Y un sábado? Los booleanos van sin comillas.",
 "pista2": "Los dos son false.",
 "feature": r"""
  Scenario Outline: ¿Es hábil el <fecha>?
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.tradingDay == <habil>

    Examples:
      | fecha      | habil |
      | 2026-10-26 | true  |
      | 2026-12-25 | ⟦false⟧ |
      | 2026-10-24 | ⟦false⟧ |
"""},
{"id": "E02", "title": "Usar la columna en un paso",
 "enunciado": "Completa el parámetro date con la columna fecha de la tabla.",
 "pista1": "La columna se escribe entre < >. Como la fecha es un texto, lleva comillas alrededor.",
 "pista2": "'<fecha>'",
 "feature": r"""
  Scenario Outline: Días hábiles de finales de octubre
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    And param date = ⟦'<fecha>'⟧
    When method get
    Then status 200
    And match response.tradingDay == true

    Examples:
      | fecha      |
      | 2026-10-26 |
      | 2026-10-27 |
      | 2026-10-28 |
"""},
{"id": "E03", "title": "Fiestas nacionales",
 "enunciado": "Completa el nombre de la fiesta nacional de Noruega (17 de mayo). Míralo en\nGET /api/v1/calendar/XOSL/holidays?year=2026.",
 "pista1": "Es el día de la Constitución. Copia el nombre exacto de la respuesta.",
 "pista2": "Día de la Constitución de Noruega",
 "feature": r"""
  Scenario Outline: <mercado> celebra el <fecha>
    Given url calendarUrl
    And path '/api/v1/calendar', '<mercado>', 'is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.holidayName == '<nombre>'

    Examples:
      | mercado | fecha      | nombre |
      | XOSL    | 2026-05-17 | ⟦Día de la Constitución de Noruega⟧ |
      | XCSE    | 2026-06-05 | Día de la Constitución de Dinamarca |
      | XHEL    | 2026-12-06 | Día de la Independencia de Finlandia |
"""},
{"id": "E04", "title": "T+N en una tabla",
 "enunciado": "Una orden del viernes 23 de octubre se liquida en T+2. ¿Qué día es?",
 "pista1": "Cuenta dos días hábiles después del viernes: el fin de semana no cuenta.",
 "pista2": "2026-10-27 (martes).",
 "feature": r"""
  Scenario Outline: <desde> más <n> días hábiles
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/next-trading-day'
    And params { date: '<desde>', offset: <n> }
    When method get
    Then status 200
    And match response.date == '<esperado>'

    Examples:
      | desde      | n | esperado   |
      | 2026-10-23 | 2 | ⟦2026-10-27⟧ |
      | 2026-12-23 | 1 | 2026-12-28 |
      | 2026-10-26 | 1 | 2026-10-27 |
"""},
{"id": "E05", "title": "Los datos, en un fichero",
 "enunciado": "Los festivos suecos están en festivos.csv, en la misma carpeta. Léelo como tabla de Examples.",
 "pista1": "read() lee ficheros relativos a la carpeta del .feature. El nombre va entre comillas.",
 "pista2": "read('festivos.csv')",
 "feature": r"""
  Scenario Outline: <nombre> no es día hábil en Estocolmo
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    And param date = '<fecha>'
    When method get
    Then status 200
    And match response.tradingDay == false
    And match response.holidayName == '<nombre>'

    Examples:
      | read(⟦'festivos.csv'⟧) |
"""},
],
}

L11 = {
"code": "L11", "slug": "cabeceras-correlacion-cache", "url": "marketUrl",
"readme": r"""
# L11 · Cabeceras, correlación y caché

**En esta lección** envías y lees cabeceras HTTP, sigues una petición con su identificador y pruebas la caché.

## La idea

Las cabeceras son datos **sobre** la petición o la respuesta. Las que más usarás en Nordvik:

| Cabecera | Quién la envía | Para qué |
| --- | --- | --- |
| `X-Correlation-Id` | tú (o el servidor si no la envías) | Seguir una petición por todos los servicios y logs |
| `X-Service`, `X-Service-Version` | el servidor | Saber quién te contestó |
| `Content-Type` | los dos | El formato del cuerpo (`application/json`, `application/problem+json`) |
| `ETag` | el servidor | La "versión" de un recurso |
| `If-None-Match` | tú | "Dámelo solo si cambió": si no, el servidor responde `304` sin cuerpo |

En Karate: `header Nombre = valor` para enviar, y `responseHeaders['Nombre'][0]` para leer (es una lista porque una cabecera puede repetirse).

## La norma

1. Envía siempre tu propio `X-Correlation-Id` en los tests: cuando algo falle, lo buscarás en los logs.
2. Un `304` no tiene cuerpo: no compruebes `response`.
3. Los nombres de cabecera se escriben como los envía el servidor (`ETag`, no `etag`).

## El fundamento

En un sistema con 156 servicios, el `correlationId` es la única forma de reconstruir qué pasó con una petición. Y la caché es una fuente clásica de bugs: datos viejos que nadie ve porque el código es `200`.

## Tus ejercicios

`academia corregir L11`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L11. Ejecútalo con:  academia ejemplo L11
@ejemplo
Feature: L11 · Cabeceras

  Scenario: Mi correlationId vuelve en la respuesta
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    And header X-Correlation-Id = 'academia-l11-ejemplo'
    When method get
    Then status 200
    And match responseHeaders['X-Correlation-Id'][0] == 'academia-l11-ejemplo'
    And match responseHeaders['X-Service'][0] == 'market-data'

  Scenario: Caché con ETag y 304
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['ETag'][0]
    Given path '/api/v1/prices', 'SE0000000001', 'latest'
    And header If-None-Match = etag
    When method get
    Then status 304
""",
"ejercicios": [
{"id": "E01", "title": "Envía tu correlationId",
 "enunciado": "Envía el correlationId 'mi-primer-id' y comprueba que vuelve en la respuesta.",
 "pista1": "El valor de una cabecera es un texto.",
 "pista2": "header X-Correlation-Id = 'mi-primer-id'",
 "feature": r"""
  Scenario: Mi identificador viaja de ida y vuelta
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    And header X-Correlation-Id = ⟦'mi-primer-id'⟧
    When method get
    Then status 200
    And match responseHeaders['X-Correlation-Id'][0] == 'mi-primer-id'
"""},
{"id": "E02", "title": "Leer una cabecera de la respuesta",
 "enunciado": "Guarda la cabecera de versión del recurso (la de la caché) en la variable etag.",
 "pista1": "La cabecera de la caché tiene 4 letras y se escribe con dos mayúsculas.",
 "pista2": "responseHeaders['ETag'][0]",
 "feature": r"""
  Scenario: El ETag del último precio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['⟦ETag⟧'][0]
    * match etag contains 'SE0000000001'
"""},
{"id": "E03", "title": "Dámelo solo si cambió",
 "enunciado": "Envía el ETag en la cabecera de caché condicional. Como el precio no ha cambiado, ¿qué código llega?",
 "pista1": "La cabecera empieza por If-. El código significa 'no modificado'.",
 "pista2": "header If-None-Match = etag y status 304.",
 "feature": r"""
  Scenario: 304 Not Modified
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002', 'latest'
    When method get
    Then status 200
    * def etag = responseHeaders['ETag'][0]
    Given path '/api/v1/prices', 'SE0000000002', 'latest'
    And header ⟦If-None-Match⟧ = etag
    When method get
    Then status ⟦304⟧
"""},
{"id": "E04", "title": "¿Quién me ha contestado?",
 "enunciado": "Comprueba con la cabecera X-Service qué servicio respondió al tipo de cambio.",
 "pista1": "Es el nombre del microservicio de tipos de cambio.",
 "pista2": "'fx-rates'",
 "feature": r"""
  Scenario: Responde fx-rates
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    When method get
    Then status 200
    And match responseHeaders['X-Service'][0] == ⟦'fx-rates'⟧
"""},
{"id": "E05", "title": "El tipo de contenido de un error",
 "enunciado": "Los errores no son application/json normal: tienen su propio tipo de contenido. Compruébalo.",
 "pista1": "Es 'application/' seguido del nombre del formato RFC 9457 y '+json'.",
 "pista2": "'application/problem+json'",
 "feature": r"""
  Scenario: Los errores tienen su propio Content-Type
    Given url catalogUrl
    And path '/api/v1/funds/hola'
    When method get
    Then status 400
    And match responseHeaders['Content-Type'][0] contains ⟦'application/problem+json'⟧
"""},
],
}

RESET = "    * configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }"

L12 = {
"code": "L12", "slug": "el-tiempo-en-tus-tests", "url": "clockUrl",
"readme": r"""
# L12 · El tiempo en tus tests: el reloj virtual

**En esta lección** controlas la hora de todo el sistema para probar reglas que dependen del tiempo, sin esperar.

## La idea

En finanzas casi todo depende de la hora: la hora de corte de las 15:00, la publicación del NAV a las 18:00, los festivos, los plazos. Esperar a las 15:00 para probar no es una opción.

En Nordvik **ningún servicio usa la hora del ordenador**: todos preguntan a `clock-service`. Y tú puedes moverlo:

| Petición | Efecto |
| --- | --- |
| `POST /api/v1/clock/freeze { "at": "2026-10-26T14:59:00+01:00" }` | Congela el reloj en ese instante |
| `POST /api/v1/clock/advance { "minutes": 2 }` | Avanza (o retrocede con negativos) |
| `POST /api/v1/clock/travel { "to": "..." }` | Salta a esa fecha y sigue corriendo |
| `POST /api/v1/clock/reset` | Vuelve a la hora real |

## La norma

1. Escribe siempre la hora con su zona: `+01:00` en invierno y `+02:00` en verano en Estocolmo.
2. **Todo test que toca el reloj lo deja como estaba.** Para eso, en el `Background`:
   `* configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }`
3. Prueba los bordes: 14:59:59 y 15:00:00, 17:59 y 18:00.

## El fundamento

Las reglas de tiempo son de las que más fallan en producción: el cambio de hora, el último día del mes, la orden que llega un segundo tarde. Un reloj controlable las convierte en tests deterministas que dan el mismo resultado hoy y dentro de un año.

## Tus ejercicios

`academia corregir L12`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L12. Ejecútalo con:  academia ejemplo L12
@ejemplo
Feature: L12 · Controlar el tiempo

  Background:
""" + RESET + r"""

  Scenario: Un minuto antes y un minuto después de la hora de corte
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == true
    Given url clockUrl
    And path '/api/v1/clock/advance'
    And request { minutes: 2 }
    When method post
    Then status 200
    And match response.time == '15:01:00'
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == false
""",
"ejercicios": [
{"id": "E01", "title": "Antes de la hora de corte",
 "enunciado": "Congelamos el reloj a las 14:59 de un lunes hábil. ¿Estamos antes de la hora de corte?",
 "pista1": "La hora de corte es a las 15:00. Los booleanos van sin comillas.",
 "pista2": "true",
 "feature": r"""
  Background:
""" + RESET + r"""

  Scenario: A las 14:59 todavía se aceptan órdenes del día
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == ⟦true⟧
"""},
{"id": "E02", "title": "Avanzar el reloj",
 "enunciado": "Desde las 14:59, avanza el reloj lo justo para pasar la hora de corte: hasta las 15:01.",
 "pista1": "¿Cuántos minutos hay de 14:59 a 15:01?",
 "pista2": "2",
 "feature": r"""
  Background:
""" + RESET + r"""

  Scenario: A las 15:01 la orden ya va al día siguiente
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given path '/api/v1/clock/advance'
    And request { minutes: ⟦2⟧ }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == false
"""},
{"id": "E03", "title": "Viajar a Nochebuena",
 "enunciado": "Viaja al 24 de diciembre y pregunta si hoy es hábil. ¿Qué razón da el calendario?",
 "pista1": "No es fin de semana: es otra razón. Mira los valores posibles de reason en Swagger.",
 "pista2": "'HOLIDAY'",
 "feature": r"""
  Background:
""" + RESET + r"""

  Scenario: Nochebuena no es día hábil
    Given url clockUrl
    And path '/api/v1/clock/travel'
    And request { to: '2026-12-24T10:00:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/is-trading-day'
    When method get
    Then status 200
    And match response.tradingDay == false
    And match response.reason == ⟦'HOLIDAY'⟧
"""},
{"id": "E04", "title": "El NAV todavía no se ha publicado",
 "enunciado": "Son las 17:59 del lunes 26 de octubre. El NAV del día se publica a las 18:00.\n¿De qué fecha es el último precio publicado?",
 "pista1": "Si el de hoy aún no está, el último es el del día hábil anterior. El 26 es lunes.",
 "pista2": "'2026-10-23' (el viernes).",
 "feature": r"""
  Background:
""" + RESET + r"""

  Scenario: A las 17:59 el último precio es el del viernes
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T17:59:00+01:00' }
    When method post
    Then status 200
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.date == ⟦'2026-10-23'⟧
"""},
{"id": "E05", "title": "El NAV ya se ha publicado",
 "enunciado": "Ahora son las 18:00 en punto del mismo lunes. ¿De qué fecha es el último precio?",
 "pista1": "A las 18:00 se publica el del propio día.",
 "pista2": "'2026-10-26'",
 "feature": r"""
  Background:
""" + RESET + r"""

  Scenario: A las 18:00 ya está el precio de hoy
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T18:00:00+01:00' }
    When method post
    Then status 200
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.date == ⟦'2026-10-26'⟧
"""},
{"id": "E06", "title": "Dejar el reloj como estaba",
 "enunciado": "Congela el reloj y después devuélvelo a la hora real. ¿Qué modo indica el reloj?",
 "pista1": "Los modos son REAL, OFFSET y FROZEN.",
 "pista2": "'REAL'",
 "feature": r"""
  Scenario: Volver a la hora real
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2030-01-01T00:00:00+01:00' }
    When method post
    Then status 200
    And match response.mode == 'FROZEN'
    Given path '/api/v1/clock/reset'
    And request {}
    When method post
    Then status 200
    And match response.mode == ⟦'REAL'⟧
"""},
],
}

AYUDANTE = r"""
# Ayudante reutilizable de L13: crea una secuencia con nombre único.
# Recibe (opcional) prefix. Devuelve: nombre y response.
@ignore
Feature: Crear una secuencia con nombre único

  Scenario: Crear secuencia
    * def prefijo = karate.get('prefix', 'GEN')
    * def nombre = 'l13-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: '#(prefijo)' }
    When method post
    Then status 201
"""

L13 = {
"code": "L13", "slug": "reutilizar-con-call", "url": "sequenceUrl",
"readme": r"""
# L13 · Reutilizar: call y features llamables

**En esta lección** escribes un paso una sola vez y lo llamas desde muchos tests.

## La idea

Cuando varios tests necesitan lo mismo (crear un cliente, pedir un token, crear una secuencia), se escribe en una feature aparte y se **llama**:

```gherkin
* def resultado = call read('_crear-secuencia.feature') { prefix: 'CLI' }
* match resultado.response.prefix == 'CLI'
* def nombre = resultado.nombre
```

- Lo que pasas entre llaves llega a la feature llamada como **variables** (`prefix`).
- Lo que devuelve `call` es un objeto con **todas** las variables de la feature llamada (`nombre`, `response`...).
- `callonce` hace lo mismo pero solo la primera vez en ese fichero; las siguientes reutiliza el resultado.
- Si pasas una **lista** de objetos, `call` se ejecuta una vez por elemento y devuelve una lista.

## La norma

1. Las features que solo sirven para ser llamadas llevan el tag `@ignore` para que no se ejecuten solas.
2. Una feature llamable hace **una** cosa y la devuelve; no comprueba reglas del test que la llama.
3. `callonce` solo para cosas caras que no cambian (un token); nunca para datos que el test modifica.

## El fundamento

Si 40 tests crean clientes copiando el mismo bloque, un cambio en la API te obliga a tocar 40 ficheros. Con una feature llamable tocas uno. Es la primera capa del framework de 5 capas del plan.

## Tus ejercicios

El ayudante `_crear-secuencia.feature` ya está en la carpeta de ejercicios. `academia corregir L13`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L13. Ejecútalo con:  academia ejemplo L13
@ejemplo
Feature: L13 · Llamar a una feature reutilizable

  Scenario: Crear una secuencia con el ayudante y usarla
    * def resultado = call read('ejercicios/_crear-secuencia.feature') { prefix: 'CLI' }
    * match resultado.response.prefix == 'CLI'
    * def nombre = resultado.nombre
    Given url sequenceUrl
    And path '/api/v1/sequences', nombre, 'next'
    When method post
    Then status 200
    And match response.values == ['CLI-0001']
""",
"apoyo": {"_crear-secuencia.feature": AYUDANTE},
"ejercicios": [
{"id": "E01", "title": "Llama al ayudante",
 "enunciado": "Llama a la feature ayudante, que está en la misma carpeta, con el prefijo X.",
 "pista1": "read() recibe el nombre del fichero entre comillas. El ayudante empieza por guion bajo.",
 "pista2": "read('_crear-secuencia.feature')",
 "feature": r"""
  Scenario: Crear con el ayudante
    * def resultado = call read(⟦'_crear-secuencia.feature'⟧) { prefix: 'X' }
    * match resultado.response.prefix == 'X'
"""},
{"id": "E02", "title": "Pasar un argumento",
 "enunciado": "Llama al ayudante para crear una secuencia de pagos, con prefijo PAGO.",
 "pista1": "El argumento es un objeto JSON con el campo prefix.",
 "pista2": "{ prefix: 'PAGO' }",
 "feature": r"""
  Scenario: Una secuencia de pagos
    * def resultado = call read('_crear-secuencia.feature') { prefix: ⟦'PAGO'⟧ }
    * match resultado.response.prefix == 'PAGO'
"""},
{"id": "E03", "title": "Usar lo que devuelve",
 "enunciado": "El ayudante guarda el nombre que inventó en su variable nombre. Recupéralo y pide el siguiente valor.\nSin prefijo, el ayudante usa GEN.",
 "pista1": "call devuelve un objeto con todas las variables del ayudante. Accede con un punto.",
 "pista2": "resultado.nombre",
 "feature": r"""
  Scenario: Usar la secuencia creada
    * def resultado = call read('_crear-secuencia.feature')
    * def nombre = resultado.⟦nombre⟧
    Given url sequenceUrl
    And path '/api/v1/sequences', nombre, 'next'
    When method post
    Then status 200
    And match response.values == ['GEN-0001']
"""},
{"id": "E04", "title": "Solo una vez",
 "enunciado": "En el Background, crea la secuencia una sola vez para todo el fichero.",
 "pista1": "Es la versión de call que se ejecuta una vez y reutiliza el resultado.",
 "pista2": "callonce",
 "feature": r"""
  Background:
    * def unica = ⟦callonce⟧ read('_crear-secuencia.feature') { prefix: 'UNA' }

  Scenario: La secuencia existe
    * match unica.response.current == 0
    * match unica.response.prefix == 'UNA'

  Scenario: Es la misma en el segundo escenario
    * match unica.response.prefix == 'UNA'
"""},
{"id": "E05", "title": "Llamar en bucle",
 "enunciado": "Crea dos secuencias, A y B, con una sola línea: pasa una lista de argumentos.",
 "pista1": "Una lista JSON va entre corchetes: [ {...}, {...} ].",
 "pista2": "[{ prefix: 'A' }, { prefix: 'B' }]",
 "feature": r"""
  Scenario: Dos secuencias de golpe
    * def lista = call read('_crear-secuencia.feature') ⟦[{ prefix: 'A' }, { prefix: 'B' }]⟧
    * match lista == '#[2]'
    * match lista[1].response.prefix == 'B'
"""},
],
}

L14 = {
"code": "L14", "slug": "funciones-javascript", "url": "fxUrl",
"readme": r"""
# L14 · Funciones JavaScript propias

**En esta lección** escribes pequeñas funciones para calcular y comprobar cosas que Karate no trae de serie.

## La idea

Karate entiende JavaScript. Una función se guarda en una variable y se usa como cualquier otra:

```gherkin
* def doble = function(x){ return x * 2 }
* match doble(21) == 42
```

También puede vivir en un fichero `.js` (que contiene **solo** la función) y leerse con `read`:

```gherkin
* def formatear = read('formatearSek.js')
* match formatear(1500) == '1500.00 SEK'
```

Y puede usarse dentro de un fuzzy matcher: `'#? esPositivo(_)'`.

Utilidades de Karate muy usadas: `karate.repeat(n, fn)`, `karate.filter`, `karate.map`, `karate.sizeOf`.

## La norma

1. Una función, una responsabilidad, un nombre que diga lo que hace.
2. Las funciones que usan varios tests van en fichero `.js`, no copiadas.
3. Cuidado con los decimales: `toFixed(2)` redondea distinto al redondeo bancario del servidor en los casos de mitad exacta (lo verás en el nivel 7).

## El fundamento

El servidor calcula; tu test **recalcula de forma independiente** y compara. Si los dos llegan al mismo número por caminos distintos, el cálculo es fiable. Si copiaras el valor de la respuesta, no estarías probando nada.

## Tus ejercicios

`academia corregir L14`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L14. Ejecútalo con:  academia ejemplo L14
@ejemplo
Feature: L14 · Recalcular de forma independiente

  Scenario: Mi conversión coincide con la del servidor
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    And param date = '2026-10-23'
    When method get
    Then status 200
    * def tipo = parseFloat(response.rate)
    Given path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '250.00', date: '2026-10-23' }
    When method get
    Then status 200
    * def convertir = function(importe, t){ return (importe * t).toFixed(2) }
    * match convertir(250, tipo) == response.converted
""",
"apoyo": {"formatearSek.js": "function(n) {\n  return n.toFixed(2) + ' SEK';\n}\n"},
"ejercicios": [
{"id": "E01", "title": "Tu primera función",
 "enunciado": "Completa la función doble para que devuelva el doble de x.",
 "pista1": "Doble es multiplicar por un número muy pequeño.",
 "pista2": "2",
 "feature": r"""
  Scenario: Doblar un número
    * def doble = function(x){ return x * ⟦2⟧ }
    * match doble(21) == 42
"""},
{"id": "E02", "title": "Recalcular una conversión",
 "enunciado": "Completa el número de decimales con el que redondear un importe en coronas.",
 "pista1": "Los importes de dinero llevan dos decimales.",
 "pista2": "toFixed(2)",
 "feature": r"""
  Scenario: 100 euros en coronas
    Given url fxUrl
    And path '/api/v1/rates/EUR/SEK'
    And param date = '2026-10-23'
    When method get
    Then status 200
    * def tipo = parseFloat(response.rate)
    Given path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100.00', date: '2026-10-23' }
    When method get
    Then status 200
    * def convertir = function(importe, t){ return (importe * t).toFixed(⟦2⟧) }
    * match convertir(100, tipo) == response.converted
"""},
{"id": "E03", "title": "Una función en un fichero",
 "enunciado": "Hay una función en formatearSek.js, en la misma carpeta. Léela y úsala.",
 "pista1": "Igual que con los CSV: read() y el nombre del fichero entre comillas.",
 "pista2": "read('formatearSek.js')",
 "feature": r"""
  Scenario: Formatear un importe
    * def formatear = read(⟦'formatearSek.js'⟧)
    * match formatear(1500) == '1500.00 SEK'
"""},
{"id": "E04", "title": "Repetir N veces",
 "enunciado": "Genera la lista [0, 10, 20] con karate.repeat.",
 "pista1": "karate.repeat(n, fn) llama a fn con i = 0, 1, ..., n-1.",
 "pista2": "3",
 "feature": r"""
  Scenario: Una lista generada
    * def lista = karate.repeat(⟦3⟧, function(i){ return i * 10 })
    * match lista == [0, 10, 20]
"""},
{"id": "E05", "title": "Tu función como comprobación",
 "enunciado": "Usa la función esPositivo dentro de un fuzzy matcher para validar el importe convertido.",
 "pista1": "Un fuzzy matcher con condición empieza por '#?' y _ es el valor.",
 "pista2": "'#? esPositivo(_)'",
 "feature": r"""
  Scenario: El importe convertido es positivo
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'NOK', to: 'SEK', amount: '500.00' }
    When method get
    Then status 200
    * def esPositivo = function(x){ return parseFloat(x) > 0 }
    * match response.converted == ⟦'#? esPositivo(_)'⟧
"""},
],
}

L15 = {
"code": "L15", "slug": "proyecto-del-tramo", "url": "catalogUrl",
"readme": r"""
# L15 · Proyecto del tramo: una suite de mercado completa

**En este proyecto** juntas todo lo aprendido en las lecciones L00-L14 en una suite real sobre los 8 servicios.

## El encargo

El equipo de Inversiones va a publicar una web de fondos. Antes, quiere una suite de regresión que garantice:

1. Que el catálogo devuelve los 14 fondos y que todos tienen una forma válida.
2. Que cada fondo abierto tiene precio publicado y que su divisa coincide entre catálogo y precios.
3. Que los festivos suecos no tienen precio.
4. Que la conversión de divisas cuadra con el tipo publicado.
5. Que la hora de corte se respeta (con el reloj virtual).
6. Que los errores tienen el formato RFC 9457.

## Cómo trabajarlo

- El fichero `ejercicios/E01.feature` tiene los 6 escenarios con huecos `___` más grandes que en las lecciones: a veces falta una línea entera.
- Puedes usar todo: `Background`, `call`, `Scenario Outline`, funciones JS, fuzzy matchers, reloj virtual.
- Cuando pase, ejecútalo varias veces: un buen test pasa siempre, no solo una vez.

## Criterios de calidad (los que usará un revisor)

- [ ] Ningún valor variable comprobado por valor (ids, fechas de hoy, NAV).
- [ ] Cada escenario se puede ejecutar solo y en cualquier orden.
- [ ] El reloj queda como estaba.
- [ ] Los nombres de escenario dicen la regla de negocio, no la técnica.

## Siguiente paso

Al terminar el proyecto has completado el **tramo 1** de la ruta (niveles 0-5 sobre la oleada 0). El siguiente tramo arranca con la oleada 1: autenticación, clientes y carteras.
""",
"ejemplo": r"""
# Ejemplo de L15: un escenario resuelto como modelo de los demás.
@ejemplo
Feature: L15 · Modelo de escenario de la suite

  Scenario: Divisa coherente entre catálogo y precios para el fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    * def divisa = response.base_currency
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    And match response.currency == divisa
""",
"ejercicios": [
{"id": "E01", "title": "Suite de regresión del mercado",
 "enunciado": "Completa los 6 escenarios de la suite. Lee el README de L15 antes de empezar.",
 "pista1": "Trabaja un escenario cada vez. Los huecos grandes son líneas completas que ya has escrito en otras lecciones.",
 "pista2": "Escenario 2: usa karate.filter para quedarte con los OPEN y call con una lista para recorrerlos, o un Scenario Outline. Escenario 5: freeze a las 14:59 y advance de 1 minuto.",
 "feature": r"""
  Background:
""" + RESET + r"""

  Scenario: El catálogo tiene 14 fondos con forma válida
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match response.total == ⟦14⟧
    And match each response.items == ⟦{ isin: '#regex [A-Z]{2}[0-9A-Z]{9}[0-9]', name: '#string', category: '#string', base_currency: '#regex [A-Z]{3}', sri: '#? _ >= 1 && _ <= 7', status: '#string', management_fee: '#regex \\d+\\.\\d{2}' }⟧

  Scenario: Cada fondo abierto tiene precio y su divisa coincide
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def abiertos = karate.filter(response.items, function(f){ return f.status == ⟦'OPEN'⟧ })
    * match abiertos == '#[13]'
    * def comprobar =
      \"\"\"
      function(f) {
        var r = karate.http(marketUrl + '/api/v1/prices/' + f.isin + '/latest').get();
        return r.status == 200 && r.body.currency == f.⟦base_currency⟧;
      }
      \"\"\"
    * def resultados = karate.map(abiertos, comprobar)
    * match each resultados == true

  Scenario Outline: <nombre> no tiene precio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002'
    And param date = '<fecha>'
    When method get
    Then status ⟦404⟧
    And match response.code == 'NO_PRICE_FOR_DATE'

    Examples:
      | fecha      | nombre              |
      | 2026-01-06 | Epifanía            |
      | 2026-06-19 | Víspera de San Juan |
      | 2026-05-14 | Ascensión           |

  Scenario: La conversión cuadra con el tipo publicado
    Given url fxUrl
    And path '/api/v1/rates/USD/SEK'
    And param date = '2026-10-23'
    When method get
    Then status 200
    * def tipo = parseFloat(response.rate)
    Given path '/api/v1/convert'
    And params { from: 'USD', to: 'SEK', amount: '1000.00', date: '2026-10-23' }
    When method get
    Then status 200
    And match response.converted == ⟦(1000 * tipo).toFixed(2)⟧

  Scenario: La hora de corte se respeta
    Given url clockUrl
    And path '/api/v1/clock/freeze'
    And request { at: '2026-10-26T14:59:00+01:00' }
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == true
    Given url clockUrl
    And path '/api/v1/clock/advance'
    And request ⟦{ minutes: 1 }⟧
    When method post
    Then status 200
    Given url calendarUrl
    And path '/api/v1/calendar/XSTO/today'
    When method get
    Then status 200
    And match response.beforeCutOff == ⟦false⟧

  Scenario: Los errores siguen el formato RFC 9457
    Given url fxUrl
    And path '/api/v1/rates/EUR/XXX'
    When method get
    Then status 400
    And match responseHeaders['Content-Type'][0] contains 'application/problem+json'
    And match response contains ⟦{ type: '#string', title: '#string', status: 400, detail: '#string', code: 'UNSUPPORTED_CURRENCY', correlationId: '#string' }⟧
"""},
],
}

LECCIONES = [L10, L11, L12, L13, L14, L15]

# Los bloques de texto multilínea de Karate usan triple comilla; en Python los escribimos escapados.
for _ex in L15["ejercicios"]:
    _ex["feature"] = _ex["feature"].replace('\\"\\"\\"', '"""')
