# Lecciones L00-L03: primeros pasos con product-catalog.

L00 = {
"code": "L00", "slug": "que-es-una-api", "url": "catalogUrl",
"readme": r"""
# L00 · ¿Qué es una API?

**En esta lección** entiendes qué pasa cuando un programa le pide algo a otro, y ejecutas tu primera prueba sin escribir casi nada.

## La idea

Una **API** es una puerta por la que un programa le hace peticiones a otro. En Nordvik, cada microservicio es una de esas puertas. Tú (o Karate) eres el **cliente**; el microservicio es el **servidor**.

Cada conversación tiene dos mitades:

| La petición (lo que tú envías) | La respuesta (lo que te devuelven) |
| --- | --- |
| **Método**: qué quieres hacer (`GET` leer, `POST` crear, `PUT`/`PATCH` cambiar, `DELETE` borrar) | **Código de estado**: un número que resume cómo ha ido |
| **URL**: a quién y qué recurso (`http://localhost:6801/api/v1/funds`) | **Cabeceras**: datos sobre la respuesta |
| **Cabeceras** (opcional): datos extra | **Cuerpo**: los datos, casi siempre en **JSON** |
| **Cuerpo** (opcional): los datos que envías | |

## La norma

1. Los códigos `2xx` significan éxito: `200` OK, `201` creado, `204` hecho y sin contenido.
2. Los códigos `4xx` significan que **el cliente** pidió algo mal: `400` petición inválida, `404` no existe, `409` conflicto, `422` regla de negocio incumplida.
3. Los códigos `5xx` significan que **el servidor** falló.
4. Una URL se lee así: `http://localhost:6801` (el servicio) + `/api/v1/funds` (el recurso) + `?category=EQUITY` (los parámetros).

## El fundamento

Probar una API es comprobar que, ante cada petición, el servidor contesta **lo que dice su contrato**: el código correcto y el JSON correcto. Un buen tester predice la respuesta antes de enviar la petición. Si la predicción falla, o el servidor tiene un bug, o tu modelo mental está mal: en los dos casos aprendes algo.

## Así se escribe en Karate

```gherkin
Scenario: Leer el catálogo
  Given url catalogUrl            # el servicio (la URL está en karate-config.js)
  And path '/api/v1/funds'        # el recurso
  When method get                 # la acción
  Then status 200                 # tu predicción del código
```

## Antes de los ejercicios

1. Arranca Nordvik: `nordvik up` (desde la carpeta `nordvik`).
2. Abre `http://localhost:6801/swagger` en el navegador y prueba `GET /api/v1/funds` con **Try it out**. Mira el código y el JSON.
3. Ejecuta el ejemplo: `academia ejemplo L00`.

## Errores típicos

- Confundir `404` (no existe) con `400` (lo pediste mal).
- Olvidar que Nordvik tiene que estar arrancado: si no, verás "No hay ningún servicio escuchando".

## Tus ejercicios

Cinco predicciones: rellena cada `___` y ejecuta `academia corregir L00`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L00. Ejecútalo con:  academia ejemplo L00
@ejemplo
Feature: L00 · Mi primera conversación con una API

  Scenario: Pedir el catálogo de fondos y ver qué contesta el servidor
    # 1. ¿A quién hablamos? A product-catalog (la URL viene de karate-config.js)
    Given url catalogUrl
    # 2. ¿Qué recurso queremos? La lista de fondos
    And path '/api/v1/funds'
    # 3. ¿Qué queremos hacer? Leer: GET
    When method get
    # 4. ¿Qué esperamos? 200 = todo bien
    Then status 200
    # 5. Miramos la respuesta: un JSON con la lista y el total
    And print 'El catálogo tiene', response.total, 'fondos'

  Scenario: Pedir un fondo que no existe
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    # El formato del ISIN es correcto pero el fondo no existe: 404
    Then status 404
    And print 'El servidor explica:', response.detail
""",
"ejercicios": [
{"id": "E01", "title": "El método para leer",
 "enunciado": "Queremos LEER la lista de fondos. ¿Qué método HTTP se usa para leer?",
 "pista1": "Leer en HTTP es el método más común de todos. Se escribe en minúsculas en Karate.",
 "pista2": "Los métodos son get, post, put, patch y delete. Para leer sin cambiar nada se usa get.",
 "feature": r"""
  Scenario: Leer la lista de fondos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method ⟦get⟧
    Then status 200
"""},
{"id": "E02", "title": "Predice el código de éxito",
 "enunciado": "Pedimos la lista de fondos, que existe y está bien pedida. ¿Qué código devolverá el servidor?",
 "pista1": "Es el código de éxito más típico: empieza por 2.",
 "pista2": "200 significa OK. 201 es para cuando se crea algo nuevo, y aquí solo leemos.",
 "feature": r"""
  Scenario: El catálogo responde bien
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status ⟦200⟧
"""},
{"id": "E03", "title": "Pide un fondo concreto",
 "enunciado": "Pide el detalle del fondo 'Nordvik Global Equity'.\nSu ISIN lo encontrarás en Swagger (GET /api/v1/funds) o en el ejemplo de la teoría.",
 "pista1": "El ISIN del fondo va al final de la ruta, después de /api/v1/funds/.",
 "pista2": "El primer fondo del catálogo tiene el ISIN SE0000000001. Un ISIN tiene 12 caracteres.",
 "feature": r"""
  Scenario: Detalle de Nordvik Global Equity
    Given url catalogUrl
    And path '/api/v1/funds/⟦SE0000000001⟧'
    When method get
    Then status 200
    And match response.name == 'Nordvik Global Equity'
"""},
{"id": "E04", "title": "Predice el código de algo que no existe",
 "enunciado": "Pedimos el fondo SE9999999999. Su formato es correcto, pero no está en el catálogo. ¿Qué código devolverá?",
 "pista1": "Es el error más famoso de internet: 'no encontrado'.",
 "pista2": "Los errores del cliente empiezan por 4. 'No encontrado' es el 404.",
 "feature": r"""
  Scenario: Un fondo que no existe
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    Then status ⟦404⟧
"""},
{"id": "E05", "title": "Predice el código de una petición mal hecha",
 "enunciado": "Ahora pedimos el fondo 'hola', que ni siquiera tiene formato de ISIN.\nNo es que no exista: es que la petición está mal formada. ¿Qué código devolverá?",
 "pista1": "Es el código de 'petición no válida': el primero de los 4xx.",
 "pista2": "404 es 'existe el formato pero no el recurso'. 400 es 'lo que pides no tiene sentido'.",
 "feature": r"""
  Scenario: Un ISIN mal formado
    Given url catalogUrl
    And path '/api/v1/funds/hola'
    When method get
    Then status ⟦400⟧
"""},
],
}

L01 = {
"code": "L01", "slug": "primera-peticion", "url": "catalogUrl",
"readme": r"""
# L01 · Tu primera petición: url, path, method y status

**En esta lección** escribes tú las cuatro piezas de cualquier petición en Karate.

## La idea

Toda prueba de API en Karate empieza igual. Son cuatro palabras clave que se escriben en este orden:

| Palabra clave | Qué hace | Ejemplo |
| --- | --- | --- |
| `url` | La dirección base del servicio | `Given url catalogUrl` |
| `path` | El recurso, en uno o varios trozos separados por comas | `And path '/api/v1/funds', isin` |
| `method` | Envía la petición con ese método | `When method get` |
| `status` | Comprueba el código de la respuesta | `Then status 200` |

`Given`, `And`, `When` y `Then` no hacen nada: solo hacen que el test se lea como una frase. También puedes usar `*` en su lugar.

## La norma

1. `url` va primero; `method` es la línea que **envía** la petición; lo que va antes la prepara y lo que va después comprueba.
2. Las URL base no se escriben a mano en los tests: viven en `karate-config.js` (`catalogUrl`, `marketUrl`...).
3. Los trozos de `path` se separan con comas. Karate pone las barras `/` entre ellos.
4. Un texto va entre comillas simples (`'latest'`); una variable va sin comillas (`isin`).

## El fundamento

Separar la URL base de la ruta te permite ejecutar los mismos tests contra tu PC, contra CI o contra preproducción cambiando solo la configuración. Es la primera regla de un framework profesional.

## Así se escribe

```gherkin
* def isin = 'SE0000000001'
Given url marketUrl
And path '/api/v1/prices', isin, 'latest'   # → /api/v1/prices/SE0000000001/latest
When method get
Then status 200
```

## Errores típicos

- Escribir `path 'api/v1/funds'` sin la barra inicial funciona igual, pero escribir la URL completa dentro de `path` no.
- Poner comillas a una variable: `path 'isin'` envía literalmente la palabra isin.
- Olvidar `method`: sin él no se envía nada.

## Tus ejercicios

Cinco ejercicios con los cuatro elementos. Corrige con `academia corregir L01`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L01. Ejecútalo con:  academia ejemplo L01
@ejemplo
Feature: L01 · Las cuatro piezas de una petición

  Scenario: Detalle de un fondo con la ruta en trozos
    * def isin = 'SE0000000002'
    Given url catalogUrl
    And path '/api/v1/funds', isin
    When method get
    Then status 200

  Scenario: El último precio de un fondo en otro microservicio
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200

  Scenario: La salud de un servicio
    Given url fxUrl
    And path '/health'
    When method get
    Then status 200
""",
"ejercicios": [
{"id": "E01", "title": "La URL del catálogo",
 "enunciado": "Completa la URL base. No la escribas a mano: usa la variable de karate-config.js para product-catalog.",
 "pista1": "Abre config/karate-config.js: hay una variable por servicio. La del catálogo termina en Url.",
 "pista2": "La variable se llama catalogUrl y va sin comillas, porque es una variable y no un texto.",
 "feature": r"""
  Scenario: Leer el catálogo con la URL de la configuración
    Given url ⟦catalogUrl⟧
    And path '/api/v1/funds'
    When method get
    Then status 200
"""},
{"id": "E02", "title": "La ruta de la lista de fondos",
 "enunciado": "Completa la ruta para leer la lista de fondos.",
 "pista1": "Es un texto, así que va entre comillas simples. Empieza por /api/v1.",
 "pista2": "La ruta es '/api/v1/funds'. Puedes comprobarla en http://localhost:6801/swagger.",
 "feature": r"""
  Scenario: Lista de fondos
    Given url catalogUrl
    And path ⟦'/api/v1/funds'⟧
    When method get
    Then status 200
"""},
{"id": "E03", "title": "Una ruta con una variable",
 "enunciado": "El ISIN ya está guardado en la variable isin. Úsala como segundo trozo de la ruta.",
 "pista1": "Los trozos del path se separan con comas, y una variable va sin comillas.",
 "pista2": "Escribe el nombre de la variable tal cual: isin. Karate la sustituye por su valor.",
 "feature": r"""
  Scenario: Detalle de un fondo guardado en una variable
    * def isin = 'SE0000000002'
    Given url catalogUrl
    And path '/api/v1/funds', ⟦isin⟧
    When method get
    Then status 200
    And match response.name == 'Nordvik Sverige Index'
"""},
{"id": "E04", "title": "Otro microservicio, otra URL",
 "enunciado": "Los precios no están en el catálogo, sino en market-data.\nUsa su URL y completa la ruta del último precio: /api/v1/prices/{isin}/latest.",
 "pista1": "En karate-config.js la URL de market-data se llama marketUrl. El último trozo de la ruta es un texto.",
 "pista2": "Huecos: marketUrl (sin comillas) y 'latest' (con comillas).",
 "feature": r"""
  Scenario: Último precio publicado
    Given url ⟦marketUrl⟧
    And path '/api/v1/prices', 'SE0000000001', ⟦'latest'⟧
    When method get
    Then status 200
    And match response.isin == 'SE0000000001'
"""},
{"id": "E05", "title": "¿Está vivo el servicio?",
 "enunciado": "Todos los servicios de Nordvik tienen una ruta que dice si están funcionando.\nPídesela a calendar-service.",
 "pista1": "Es una ruta corta, sin /api/v1. Mírala en Swagger, en el grupo 'Plataforma'.",
 "pista2": "La ruta es '/health' y devuelve { \"status\": \"UP\" }.",
 "feature": r"""
  Scenario: Salud de calendar-service
    Given url calendarUrl
    And path ⟦'/health'⟧
    When method get
    Then status 200
    And match response.status == 'UP'
"""},
],
}

L02 = {
"code": "L02", "slug": "leer-la-respuesta", "url": "catalogUrl",
"readme": r"""
# L02 · Leer la respuesta: response, print y match

**En esta lección** dejas de mirar solo el código de estado y compruebas **el contenido** de la respuesta.

## La idea

Después de `method`, Karate guarda el cuerpo de la respuesta en la variable `response`. Si es JSON, puedes navegarlo con puntos: `response.name`, `response.base_currency`.

`match` compara un valor con lo que esperas. Si no coinciden, el test falla y te dice qué llegó y qué esperabas.

## La norma

1. `match A == B` exige que A y B sean **exactamente** iguales: mismo valor y mismo tipo.
2. En JSON, `5` (número) y `'5'` (texto) **no** son iguales. Mira el JSON real antes de escribir el valor esperado.
3. Los nombres de campo se escriben tal y como vienen: este equipo usa `snake_case` (`base_currency`, no `baseCurrency`).
4. `print` escribe en el log; úsalo para mirar, nunca para comprobar.

## El fundamento

Un `200` solo dice que el servidor no se quejó; no dice que los datos sean correctos. Un fondo con el nombre equivocado, la divisa equivocada o el riesgo equivocado también devuelve `200`. Por eso cada test debe comprobar el dato que importa al negocio.

## Así se escribe

```gherkin
Given url catalogUrl
And path '/api/v1/funds', 'SE0000000001'
When method get
Then status 200
And print response
And match response.name == 'Nordvik Global Equity'
And match response.sri == 4
* def fondo = response
* match fondo.base_currency == 'SEK'
```

## Errores típicos

- `match response.sri == '4'`: falla, porque `sri` es un número.
- `match response.baseCurrency == 'SEK'`: falla, porque el campo se llama `base_currency`.
- Comparar mayúsculas distintas: `'open'` no es `'OPEN'`.

## Tus ejercicios

Abre el fondo en Swagger, mira el JSON y luego rellena los huecos. `academia corregir L02`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L02. Ejecútalo con:  academia ejemplo L02
@ejemplo
Feature: L02 · Comprobar el contenido de la respuesta

  Scenario: El fondo global tiene los datos esperados
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    # Mirar (no comprueba nada, solo escribe en el log)
    And print response
    # Comprobar texto
    And match response.name == 'Nordvik Global Equity'
    # Comprobar número: sin comillas
    And match response.sri == 4
    # Los campos de este equipo van en snake_case
    And match response.base_currency == 'SEK'
    # Guardar la respuesta en una variable con nombre propio
    * def fondo = response
    * match fondo.status == 'OPEN'
""",
"ejercicios": [
{"id": "E01", "title": "El nombre de un fondo",
 "enunciado": "El fondo SE0000000002 se llama 'Nordvik Sverige Index'. Compruébalo.",
 "pista1": "El valor esperado es un texto: va entre comillas simples, con las mayúsculas exactas.",
 "pista2": "Escribe 'Nordvik Sverige Index' entre comillas simples.",
 "feature": r"""
  Scenario: Nombre del fondo índice sueco
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000002'
    When method get
    Then status 200
    And match response.name == ⟦'Nordvik Sverige Index'⟧
"""},
{"id": "E02", "title": "Un número no es un texto",
 "enunciado": "Comprueba que el riesgo (sri) del fondo SE0000000002 es 5. Ojo: en el JSON es un número.",
 "pista1": "Mira el JSON en Swagger: \"sri\": 5 no lleva comillas.",
 "pista2": "Escribe 5 sin comillas. Con comillas ('5') sería un texto y no coincidiría.",
 "feature": r"""
  Scenario: Riesgo del fondo índice sueco
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000002'
    When method get
    Then status 200
    And match response.sri == ⟦5⟧
"""},
{"id": "E03", "title": "La divisa de un fondo emergente",
 "enunciado": "El fondo SE0000000004 (Nordvik Emerging Markets) cotiza en dólares. Compruébalo con el campo de divisa.",
 "pista1": "El campo se llama base_currency (snake_case) y la divisa usa el código ISO de 3 letras.",
 "pista2": "El código ISO del dólar estadounidense es 'USD'.",
 "feature": r"""
  Scenario: Divisa del fondo de emergentes
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000004'
    When method get
    Then status 200
    And match response.base_currency == ⟦'USD'⟧
"""},
{"id": "E04", "title": "Guarda la respuesta en una variable",
 "enunciado": "Guarda la respuesta completa en la variable fondo y comprueba con ella que el fondo está abierto.",
 "pista1": "Después de 'method', Karate guarda el cuerpo en una variable que ya conoces.",
 "pista2": "El valor es la variable response, sin comillas: * def fondo = response",
 "feature": r"""
  Scenario: Tech Growth está abierto a inversiones
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000006'
    When method get
    Then status 200
    * def fondo = ⟦response⟧
    * match fondo.status == 'OPEN'
"""},
{"id": "E05", "title": "Un fondo cerrado",
 "enunciado": "El fondo SE0000000007 (Nordvik Frontier Markets) ya no admite inversiones. ¿Qué dice su campo status?",
 "pista1": "Si un fondo abierto es 'OPEN', uno cerrado es su contrario en inglés y en mayúsculas.",
 "pista2": "El valor es 'CLOSED'.",
 "feature": r"""
  Scenario: Frontier Markets está cerrado
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000007'
    When method get
    Then status 200
    And match response.status == ⟦'CLOSED'⟧
"""},
],
}

L03 = {
"code": "L03", "slug": "navegar-json-y-contains", "url": "catalogUrl",
"readme": r"""
# L03 · Navegar por el JSON y match contains

**En esta lección** te mueves dentro de listas y objetos anidados, y aprendes a comprobar solo una parte de la respuesta.

## La idea

Un JSON real tiene objetos dentro de objetos y listas de objetos. Se navega así:

| Quieres | Escribes |
| --- | --- |
| Un campo | `response.name` |
| El primer elemento de una lista | `response.fees[0]` (las listas empiezan en **0**) |
| Un campo del tercer elemento | `response.fees[2].type` |
| El mismo campo de **todos** los elementos | `response.fees[*].type` → una lista |

Y `match` tiene variantes:

| Variante | Significa |
| --- | --- |
| `==` | exactamente igual |
| `contains` | contiene al menos esto (sobra lo demás) |
| `contains only` | contiene exactamente estos elementos, en cualquier orden |
| `!contains` | no contiene esto |

## La norma

1. Usa `==` cuando todo el valor importa; usa `contains` cuando solo te importa una parte.
2. `contains` con un objeto comprueba que esos campos están con esos valores.
3. `contains` con una lista comprueba que esos elementos están en la lista.

## El fundamento

Si comparas el objeto entero con `==`, tu test se rompe cada vez que el backend añade un campo nuevo, aunque nada esté mal. Comprobar solo lo que importa hace los tests **robustos**: fallan cuando hay un problema, no cuando hay un cambio inocente.

## Así se escribe

```gherkin
And match response.fees[0].type == 'MANAGEMENT'
And match response contains { category: 'EQUITY', sri: 4 }
And match response.fees contains { type: 'ENTRY', percent: '0.00' }
And match response.fees[*].type contains only ['EXIT', 'ENTRY', 'MANAGEMENT']
```

## Errores típicos

- Contar desde 1: el primer elemento es `[0]`.
- Usar `==` con una lista desordenada: `contains only` no mira el orden.

## Tus ejercicios

Seis ejercicios. `academia corregir L03`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L03. Ejecútalo con:  academia ejemplo L03
@ejemplo
Feature: L03 · Moverse por el JSON

  Scenario: Las comisiones del fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    # La primera comisión (las listas empiezan en 0)
    And match response.fees[0].type == 'MANAGEMENT'
    # Solo nos importan dos campos: contains
    And match response contains { category: 'EQUITY', sri: 4 }
    # Un objeto concreto dentro de la lista
    And match response.fees contains { type: 'MANAGEMENT', percent: '0.45' }
    # Todos los tipos de comisión, sin importar el orden
    And match response.fees[*].type contains only ['EXIT', 'ENTRY', 'MANAGEMENT']
    # Lo que NO debe estar
    And match response.fees[*].type !contains 'PERFORMANCE'
""",
"ejercicios": [
{"id": "E01", "title": "El primer elemento de una lista",
 "enunciado": "Comprueba que la primera comisión del fondo SE0000000001 es la de gestión (MANAGEMENT).",
 "pista1": "¿En qué número empiezan las listas en JSON y JavaScript?",
 "pista2": "El primer elemento es el 0: fees[0].",
 "feature": r"""
  Scenario: La primera comisión es la de gestión
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response.fees[⟦0⟧].type == 'MANAGEMENT'
"""},
{"id": "E02", "title": "El tercer elemento",
 "enunciado": "La tercera comisión (fees[2]) es la de salida. ¿Cómo se llama su tipo? Míralo en Swagger.",
 "pista1": "Las tres comisiones son gestión, entrada y salida, en inglés y en mayúsculas.",
 "pista2": "Salida en inglés es EXIT: el valor es 'EXIT'.",
 "feature": r"""
  Scenario: La tercera comisión es la de salida
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response.fees[2].type == ⟦'EXIT'⟧
"""},
{"id": "E03", "title": "Comprobar solo una parte",
 "enunciado": "Solo nos importan dos campos del fondo: que sea de renta variable (EQUITY) y de riesgo 4.\nUsa la variante de match que permite que sobren campos.",
 "pista1": "Con == tendrías que escribir el objeto entero. Hay una palabra que significa 'contiene'.",
 "pista2": "La palabra es contains: match response contains { ... }",
 "feature": r"""
  Scenario: Categoría y riesgo del fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response ⟦contains⟧ { category: 'EQUITY', sri: 4 }
"""},
{"id": "E04", "title": "Un objeto dentro de una lista",
 "enunciado": "El fondo SE0000000003 (Nordic Small Cap) cobra un 1.00 % de entrada.\nComprueba que su lista de comisiones contiene ese objeto.",
 "pista1": "Escribe un objeto JSON con los dos campos de una comisión: type y percent.",
 "pista2": "El objeto es { type: 'ENTRY', percent: '1.00' }. Ojo: percent es un texto.",
 "feature": r"""
  Scenario: Comisión de entrada del fondo de pequeñas compañías
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000003'
    When method get
    Then status 200
    And match response.fees contains ⟦{ type: 'ENTRY', percent: '1.00' }⟧
"""},
{"id": "E05", "title": "Un valor dentro de todos los elementos",
 "enunciado": "El fondo monetario tiene el ISIN SE0000000301. Comprueba que está en la lista de ISIN del catálogo.",
 "pista1": "response.items[*].isin es la lista con el ISIN de cada fondo. Busca un texto en ella.",
 "pista2": "El valor buscado es 'SE0000000301'.",
 "feature": r"""
  Scenario: El fondo monetario está en el catálogo
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match response.items[*].isin contains ⟦'SE0000000301'⟧
"""},
{"id": "E06", "title": "Exactamente estos, en cualquier orden",
 "enunciado": "Comprueba que los tipos de comisión son exactamente EXIT, MANAGEMENT y ENTRY, sin importar el orden\ny sin que falte ni sobre ninguno.",
 "pista1": "contains permite que sobren; == exige el orden. Hay una variante con dos palabras.",
 "pista2": "La variante es contains only.",
 "feature": r"""
  Scenario: Los tres tipos de comisión
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000201'
    When method get
    Then status 200
    And match response.fees[*].type ⟦contains only⟧ ['EXIT', 'MANAGEMENT', 'ENTRY']
"""},
],
}

LECCIONES = [L00, L01, L02, L03]
