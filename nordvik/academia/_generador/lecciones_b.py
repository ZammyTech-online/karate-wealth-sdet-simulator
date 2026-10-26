# Lecciones L04-L09.

L04 = {
"code": "L04", "slug": "parametros-de-consulta", "url": "catalogUrl",
"readme": r"""
# L04 · Parámetros de consulta: param y params

**En esta lección** filtras, ordenas y paginas resultados enviando parámetros en la URL.

## La idea

Los parámetros de consulta van después de `?` en la URL: `/api/v1/funds?category=EQUITY&page_size=5`. En Karate no los escribes en la ruta: los añades con `param` (uno) o `params` (varios a la vez).

| Escribes | Karate envía |
| --- | --- |
| `param category = 'EQUITY'` | `?category=EQUITY` |
| `params { currency: 'USD', min_sri: 7 }` | `?currency=USD&min_sri=7` |

El catálogo admite: `category`, `currency`, `min_sri`, `max_sri`, `status`, `sort` (`name`, `fee`, `sri`, `launch_date`), `order` (`asc`, `desc`), `page` y `page_size` (1-100).

## La norma

1. Nunca concatenes parámetros dentro de `path`: usa `param` o `params`.
2. El nombre del parámetro va **sin** comillas y su valor **con** comillas si es texto.
3. Este equipo usa `snake_case` también en los parámetros: `page_size`, no `pageSize`.
4. Un parámetro con un valor no admitido es un `400`, no un `200` vacío.

## El fundamento

Los filtros son una de las mayores fuentes de bugs: un filtro que se ignora devuelve `200` con datos que parecen correctos. Por eso no basta con mirar el código: compruebas que **todos** los resultados cumplen el filtro y que el total es el esperado.

## Así se escribe

```gherkin
Given url catalogUrl
And path '/api/v1/funds'
And param category = 'EQUITY'
And param sort = 'fee'
When method get
Then status 200
And match response.total == 7
```

## Errores típicos

- `param pageSize = 5`: el servidor no lo reconoce y lo ignora en silencio.
- Esperar un orden sin pedirlo: sin `sort`, el orden es por nombre.

## Tus ejercicios

`academia corregir L04`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L04. Ejecútalo con:  academia ejemplo L04
@ejemplo
Feature: L04 · Filtrar, ordenar y paginar

  Background:
    * url catalogUrl
    * path '/api/v1/funds'

  Scenario: Solo renta variable, la más barata primero
    Given param category = 'EQUITY'
    And param sort = 'fee'
    When method get
    Then status 200
    And match response.total == 7
    And match response.items[0].name == 'Nordvik Sverige Index'

  Scenario: Varios parámetros de una vez
    Given params { currency: 'SEK', max_sri: 2 }
    When method get
    Then status 200
    And match response.items[*].isin contains only ['SE0000000101', 'SE0000000301']

  Scenario: Paginación
    Given params { page: 2, page_size: 5 }
    When method get
    Then status 200
    And match response.page == 2
    And match response.total_pages == 3
""",
"ejercicios": [
{"id": "E01", "title": "Filtrar por categoría",
 "enunciado": "Pide solo los fondos de renta fija. Hay 3.",
 "pista1": "Las categorías van en inglés y en mayúsculas, con guion bajo si tienen dos palabras.",
 "pista2": "Renta fija es FIXED_INCOME: param category = 'FIXED_INCOME'.",
 "feature": r"""
  Scenario: Los fondos de renta fija
    Given url catalogUrl
    And path '/api/v1/funds'
    And param category = ⟦'FIXED_INCOME'⟧
    When method get
    Then status 200
    And match response.total == 3
"""},
{"id": "E02", "title": "El nombre exacto del parámetro",
 "enunciado": "Pide páginas de 5 fondos. El parámetro del tamaño de página sigue la convención snake_case del equipo.",
 "pista1": "Si escribes mal el nombre, el servidor lo ignora y devuelve 20 por página.",
 "pista2": "Es page_size, sin comillas: param page_size = 5.",
 "feature": r"""
  Scenario: Páginas de 5
    Given url catalogUrl
    And path '/api/v1/funds'
    And param ⟦page_size⟧ = 5
    When method get
    Then status 200
    And match response.page_size == 5
    And match karate.sizeOf(response.items) == 5
"""},
{"id": "E03", "title": "Varios parámetros de una vez",
 "enunciado": "Con un solo params, pide los fondos en dólares (USD) con riesgo mínimo 7. Hay 2.",
 "pista1": "params recibe un objeto JSON: { nombre: valor, nombre: valor }.",
 "pista2": "El objeto es { currency: 'USD', min_sri: 7 }.",
 "feature": r"""
  Scenario: Fondos en dólares de máximo riesgo
    Given url catalogUrl
    And path '/api/v1/funds'
    And params ⟦{ currency: 'USD', min_sri: 7 }⟧
    When method get
    Then status 200
    And match response.total == 2
"""},
{"id": "E04", "title": "Del más caro al más barato",
 "enunciado": "Ordena por comisión de forma descendente. El primero debe ser el más caro (1.75 %).",
 "pista1": "El orden se controla con el parámetro order. Ascendente es 'asc'.",
 "pista2": "Descendente es 'desc'.",
 "feature": r"""
  Scenario: El fondo más caro primero
    Given url catalogUrl
    And path '/api/v1/funds'
    And param sort = 'fee'
    And param order = ⟦'desc'⟧
    When method get
    Then status 200
    And match response.items[0].management_fee == '1.75'
"""},
{"id": "E05", "title": "Un parámetro fuera de rango",
 "enunciado": "Pedimos páginas de 500, pero el máximo es 100. Predice el código y el campo code del error.",
 "pista1": "No es que no exista: la petición es inválida. El code lo verás en Swagger o ejecutando con print response.",
 "pista2": "El código es 400 y el code es 'INVALID_QUERY'.",
 "feature": r"""
  Scenario: page_size demasiado grande
    Given url catalogUrl
    And path '/api/v1/funds'
    And param page_size = 500
    When method get
    Then status ⟦400⟧
    And match response.code == ⟦'INVALID_QUERY'⟧
"""},
],
}

L05 = {
"code": "L05", "slug": "variables-y-background", "url": "marketUrl",
"readme": r"""
# L05 · Variables, expresiones y Background

**En esta lección** guardas valores en variables, haces pequeños cálculos y evitas repetir líneas con `Background`.

## La idea

- `* def nombre = valor` crea una variable. El valor puede ser un texto, un número, un JSON o una expresión JavaScript.
- Cualquier parte de la respuesta se puede guardar: `* def nav = response.nav`.
- `Background` son pasos que Karate ejecuta **antes de cada** escenario del fichero.
- `assert` comprueba una condición JavaScript: `* assert nav.length > 4`.

## La norma

1. Los nombres de variable no llevan comillas y no empiezan por número.
2. Lo que se repite en todos los escenarios va en `Background`; lo que es propio de un escenario, no.
3. `Background` se ejecuta de nuevo para cada escenario: las variables no pasan de un escenario a otro.
4. Usa `match` para comparar valores y `assert` solo para condiciones (mayor que, menor que...).

## El fundamento

Cada escenario debe poder ejecutarse solo y en cualquier orden. Por eso Karate repite el `Background` en vez de compartir variables entre escenarios: es la base de la ejecución en paralelo que verás en el nivel Senior.

## Así se escribe

```gherkin
Background:
  * url marketUrl
  * def isin = 'SE0000000001'

Scenario: El NAV es un texto con 4 decimales
  Given path '/api/v1/prices', isin, 'latest'
  When method get
  Then status 200
  * def nav = response.nav
  * def navNumero = parseFloat(nav)
  * assert navNumero > 0
```

## Errores típicos

- Esperar que una variable de un escenario exista en el siguiente.
- Comparar `response.nav` con un número: el NAV llega como **texto** (`"154.8331"`) para no perder decimales.

## Tus ejercicios

`academia corregir L05`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L05. Ejecútalo con:  academia ejemplo L05
@ejemplo
Feature: L05 · Variables y Background

  Background:
    * url marketUrl
    * def isin = 'SE0000000001'

  Scenario: El último precio, guardado en variables
    Given path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200
    * def nav = response.nav
    * def navNumero = parseFloat(nav)
    * assert navNumero > 0
    * print 'El NAV de', isin, 'es', nav

  Scenario: El precio de una fecha construida con una expresión
    * def mes = '03'
    * def fecha = '2026-' + mes + '-02'
    Given path '/api/v1/prices', isin
    And param date = fecha
    When method get
    Then status 200
    And match response.date == fecha
""",
"ejercicios": [
{"id": "E01", "title": "Una variable para el ISIN",
 "enunciado": "Guarda en la variable isin el ISIN del fondo Nordvik Europe ESG (SE0000000005). Cotiza en euros.",
 "pista1": "Un ISIN es un texto: va entre comillas simples.",
 "pista2": "* def isin = 'SE0000000005'",
 "feature": r"""
  Scenario: El fondo europeo cotiza en euros
    * def isin = ⟦'SE0000000005'⟧
    Given url marketUrl
    And path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200
    And match response.currency == 'EUR'
"""},
{"id": "E02", "title": "Guardar un campo de la respuesta",
 "enunciado": "Guarda el NAV del último precio en la variable nav.",
 "pista1": "El NAV está en el campo nav de la respuesta.",
 "pista2": "* def nav = response.nav",
 "feature": r"""
  Scenario: El NAV es un texto con decimales
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001', 'latest'
    When method get
    Then status 200
    * def nav = ⟦response.nav⟧
    * assert nav.length > 4
    * match nav contains '.'
"""},
{"id": "E03", "title": "De texto a número",
 "enunciado": "El NAV llega como texto. Conviértelo a número con la función JavaScript que lee decimales.",
 "pista1": "En JavaScript hay parseInt (enteros) y otra para números con decimales.",
 "pista2": "parseFloat(response.nav)",
 "feature": r"""
  Scenario: Calcular con el NAV
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000002', 'latest'
    When method get
    Then status 200
    * def navNumero = ⟦parseFloat(response.nav)⟧
    * assert typeof navNumero == 'number'
    * assert navNumero > 0
"""},
{"id": "E04", "title": "Lo común, en el Background",
 "enunciado": "Los dos escenarios usan el mismo servicio. Pon su URL en el Background.",
 "pista1": "Es la variable de karate-config.js de market-data.",
 "pista2": "* url marketUrl",
 "feature": r"""
  Background:
    * url ⟦marketUrl⟧
    * def isin = 'SE0000000001'

  Scenario: Último precio
    Given path '/api/v1/prices', isin, 'latest'
    When method get
    Then status 200

  Scenario: Historial de marzo de 2026 (22 días hábiles)
    Given path '/api/v1/prices', isin, 'history'
    And param from = '2026-03-01'
    And param to = '2026-03-31'
    When method get
    Then status 200
    And match response.count == 22
"""},
{"id": "E05", "title": "Construir un valor con una expresión",
 "enunciado": "Construye la fecha 2026-03-02 uniendo tres trozos de texto. Falta el del mes.",
 "pista1": "Los meses van con dos cifras.",
 "pista2": "El trozo que falta es '03'.",
 "feature": r"""
  Scenario: Precio del 2 de marzo
    * def fecha = '2026-' + ⟦'03'⟧ + '-02'
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = fecha
    When method get
    Then status 200
    And match response.date == '2026-03-02'
"""},
],
}

L06 = {
"code": "L06", "slug": "los-errores-tambien-se-prueban", "url": "marketUrl",
"readme": r"""
# L06 · Los errores también se prueban: 400, 404 y RFC 9457

**En esta lección** pruebas que el sistema falla **bien**: con el código correcto y un mensaje útil.

## La idea

Todos los servicios de Nordvik devuelven los errores con el mismo formato, el estándar **RFC 9457 (Problem Details)**:

```json
{
  "type": "https://errors.nordvik.local/no-price-for-date",
  "title": "No encontrado",
  "status": 404,
  "detail": "El 2026-06-19 no es día hábil en XSTO (Víspera de San Juan).",
  "instance": "/api/v1/prices/SE0000000001",
  "code": "NO_PRICE_FOR_DATE",
  "correlationId": "corr-1cc160ccd05a4492",
  "service": "market-data",
  "hint": "No hay NAV en días no hábiles..."
}
```

`code` es lo que tu test debe comprobar; `detail` es para personas; `hint` es la pista del **modo didáctico** (desaparece en el nivel 9 o con la cabecera `X-Learning-Mode: false`).

## La norma

1. Cada caso negativo comprueba **el código HTTP y el `code`** del error.
2. No compruebes el texto exacto de `detail`: puede cambiar sin que sea un bug.
3. Un error de cliente nunca debe ser `500`.

## El fundamento

En producción, la mitad de los incidentes vienen de casos que nadie probó: fechas festivas, divisas raras, importes con coma. Un sistema financiero que acepta `100,00` como `10000` es un desastre. Los tests negativos son los que protegen el dinero.

## Así se escribe

```gherkin
Given url marketUrl
And path '/api/v1/prices', 'SE0000000001'
And param date = '2026-06-19'
When method get
Then status 404
And match response.code == 'NO_PRICE_FOR_DATE'
And match response.status == 404
```

## Tus ejercicios

`academia corregir L06`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L06. Ejecútalo con:  academia ejemplo L06
@ejemplo
Feature: L06 · Probar errores

  Scenario: No hay precio en un festivo
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status 404
    And match response.code == 'NO_PRICE_FOR_DATE'
    And match response.status == 404
    And print 'Pista del servidor:', response.hint

  Scenario: Un importe con coma es inválido
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100,00' }
    When method get
    Then status 400
    And match response.code == 'INVALID_AMOUNT'
""",
"ejercicios": [
{"id": "E01", "title": "Un festivo no tiene precio",
 "enunciado": "Pide el NAV del 19 de junio de 2026 (Víspera de San Juan, festivo en Suecia).\nPredice el código HTTP y el code del error.",
 "pista1": "El recurso 'precio de ese día' no existe. El code lo dice en inglés: no hay precio para la fecha.",
 "pista2": "404 y 'NO_PRICE_FOR_DATE'.",
 "feature": r"""
  Scenario: Sin precio en la Víspera de San Juan
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status ⟦404⟧
    And match response.code == ⟦'NO_PRICE_FOR_DATE'⟧
"""},
{"id": "E02", "title": "Una divisa no soportada",
 "enunciado": "fx-rates no trabaja con yenes (JPY). Pide el tipo EUR/JPY y predice el error.",
 "pista1": "La petición es inválida (no es que falte algo). El code dice 'divisa no soportada' en inglés.",
 "pista2": "400 y 'UNSUPPORTED_CURRENCY'.",
 "feature": r"""
  Scenario: El yen no está soportado
    Given url fxUrl
    And path '/api/v1/rates/EUR/JPY'
    When method get
    Then status ⟦400⟧
    And match response.code == ⟦'UNSUPPORTED_CURRENCY'⟧
"""},
{"id": "E03", "title": "La forma de un error",
 "enunciado": "Pide un fondo que no existe y comprueba dos campos del formato RFC 9457: title y status.",
 "pista1": "Ejecuta con print response o mira la teoría: title es una frase corta en español.",
 "pista2": "title es 'No encontrado' y status es el número 404 (sin comillas).",
 "feature": r"""
  Scenario: Problem Details de un fondo inexistente
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    Then status 404
    And match response.code == 'FUND_NOT_FOUND'
    And match response.title == ⟦'No encontrado'⟧
    And match response.status == ⟦404⟧
"""},
{"id": "E04", "title": "Leer la pista del servidor",
 "enunciado": "En modo didáctico, el error trae un hint. Comprueba que te sugiere el día hábil anterior al 19 de junio.",
 "pista1": "El 19 de junio de 2026 es viernes; el día hábil anterior es el día antes.",
 "pista2": "El hint contiene '2026-06-18'.",
 "feature": r"""
  Scenario: La pista sugiere el día hábil anterior
    Given url marketUrl
    And path '/api/v1/prices', 'SE0000000001'
    And param date = '2026-06-19'
    When method get
    Then status 404
    And match response.hint contains ⟦'2026-06-18'⟧
"""},
{"id": "E05", "title": "Coma decimal: un error clásico",
 "enunciado": "En España escribimos 100,00, pero la API solo admite punto decimal. Predice el código.",
 "pista1": "El servidor no puede entender el importe: la petición es inválida.",
 "pista2": "400.",
 "feature": r"""
  Scenario: Importe con coma
    Given url fxUrl
    And path '/api/v1/convert'
    And params { from: 'EUR', to: 'SEK', amount: '100,00' }
    When method get
    Then status ⟦400⟧
    And match response.code == 'INVALID_AMOUNT'
"""},
{"id": "E06", "title": "Apagar el modo didáctico",
 "enunciado": "Envía la cabecera X-Learning-Mode con el valor false. El error ya no debe traer hint.",
 "pista1": "Las cabeceras se envían con header Nombre = valor, y el valor es un texto.",
 "pista2": "header X-Learning-Mode = 'false'",
 "feature": r"""
  Scenario: Sin pistas en modo profesional
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    And header X-Learning-Mode = ⟦'false'⟧
    When method get
    Then status 404
    And match response.code == 'FUND_NOT_FOUND'
    And assert response.hint == null
"""},
],
}

L07 = {
"code": "L07", "slug": "enviar-datos-post", "url": "sequenceUrl",
"readme": r"""
# L07 · Enviar datos: POST, request y cuerpos JSON

**En esta lección** creas recursos con `POST`, los usas, y los borras.

## La idea

Hasta ahora solo leías. Para crear algo hay que **enviar un cuerpo** JSON con `request` y usar `method post`. Si sale bien, el servidor devuelve `201 Created` y, en la cabecera `Location`, dónde vive lo que creaste.

Usamos `sequence-service`, que crea contadores con nombre: `CLI-0001`, `CLI-0002`...

| Acción | Método y ruta | Cuerpo | Código si va bien |
| --- | --- | --- | --- |
| Crear | `POST /api/v1/sequences` | `{ name, prefix, padding }` | `201` |
| Siguiente valor | `POST /api/v1/sequences/{name}/next?count=N` | — | `200` |
| Ver | `GET /api/v1/sequences/{name}` | — | `200` |
| Borrar | `DELETE /api/v1/sequences/{name}` | — | `204` |

## La norma

1. Lo que creas en un test debe tener un **nombre único** para que tu test no choque con otro: usa un UUID.
2. `#(variable)` dentro de un JSON inserta el valor de una variable (**expresión embebida**).
3. Comprueba siempre el código de creación (`201`) y lo que devuelve.
4. Si puedes, borra lo que creaste al terminar.

## El fundamento

Un test que crea `'clientes'` funciona la primera vez y falla la segunda con `409 Conflict`. Los datos únicos por ejecución son lo que separa un test frágil de uno que puedes lanzar mil veces y en paralelo.

## Así se escribe

```gherkin
* def nombre = 'demo-' + java.util.UUID.randomUUID().toString().substring(0, 8)
Given url sequenceUrl
And path '/api/v1/sequences'
And request { name: '#(nombre)', prefix: 'CLI', padding: 4 }
When method post
Then status 201
And match response.current == 0
```

## Errores típicos

- Usar siempre el mismo nombre: el segundo intento da `409`.
- Olvidar `request`: el servidor recibe un cuerpo vacío y responde `400`.

## Tus ejercicios

`academia corregir L07`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L07. Ejecútalo con:  academia ejemplo L07
@ejemplo
Feature: L07 · Crear, usar y borrar

  Background:
    * url sequenceUrl
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)

  Scenario: El ciclo de vida completo de una secuencia
    # Crear
    Given path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: 'CLI', padding: 4 }
    When method post
    Then status 201
    And match response.current == 0
    And match responseHeaders['Location'][0] == '/api/v1/sequences/' + nombre
    # Usar
    Given path '/api/v1/sequences', nombre, 'next'
    And param count = 2
    When method post
    Then status 200
    And match response.values == ['CLI-0001', 'CLI-0002']
    # Borrar
    Given path '/api/v1/sequences', nombre
    When method delete
    Then status 204
""",
"ejercicios": [
{"id": "E01", "title": "Tu primer cuerpo JSON",
 "enunciado": "Crea una secuencia con el prefijo CLI. El nombre único ya está en la variable nombre.",
 "pista1": "Dentro del JSON, los textos van entre comillas simples.",
 "pista2": "prefix: 'CLI'",
 "feature": r"""
  Scenario: Crear una secuencia con prefijo
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: ⟦'CLI'⟧ }
    When method post
    Then status 201
    And match response.prefix == 'CLI'
"""},
{"id": "E02", "title": "El método para crear y su código",
 "enunciado": "Completa el método que crea recursos y el código que devuelve el servidor cuando crea algo.",
 "pista1": "Para crear no se usa get. Y el código no es 200: es el de 'creado'.",
 "pista2": "method post y status 201.",
 "feature": r"""
  Scenario: Crear devuelve 201
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method ⟦post⟧
    Then status ⟦201⟧
"""},
{"id": "E03", "title": "Pedir los siguientes valores",
 "enunciado": "Crea una secuencia con prefijo A y pide 2 valores. Completa el último trozo de la ruta\ny la lista de valores que esperas (relleno de 4 cifras).",
 "pista1": "La ruta es /api/v1/sequences/{name}/next. Los valores llevan prefijo, guion y 4 cifras.",
 "pista2": "El trozo es 'next' y la lista es ['A-0001', 'A-0002'].",
 "feature": r"""
  Scenario: Dos valores seguidos
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)', prefix: 'A' }
    When method post
    Then status 201
    Given path '/api/v1/sequences', nombre, ⟦'next'⟧
    And param count = 2
    When method post
    Then status 200
    And match response.values == ⟦['A-0001', 'A-0002']⟧
"""},
{"id": "E04", "title": "Crear dos veces lo mismo",
 "enunciado": "Intenta crear dos veces la misma secuencia. ¿Qué código devuelve el segundo intento?",
 "pista1": "No es que la petición esté mal ni que algo no exista: choca con algo que ya existe.",
 "pista2": "409 Conflict.",
 "feature": r"""
  Scenario: Un nombre repetido
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status 201
    Given path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status ⟦409⟧
    And match response.code == 'SEQUENCE_ALREADY_EXISTS'
"""},
{"id": "E05", "title": "¿Qué campo está mal?",
 "enunciado": "Los nombres solo admiten minúsculas, números y guiones. Envía uno con mayúsculas\ny comprueba qué campo señala el error.",
 "pista1": "Los errores de validación traen una lista errors con field y message.",
 "pista2": "El campo es 'name'.",
 "feature": r"""
  Scenario: Nombre con mayúsculas
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: 'Mayusculas' }
    When method post
    Then status 400
    And match response.code == 'VALIDATION_FAILED'
    And match response.errors[0].field == ⟦'name'⟧
"""},
{"id": "E06", "title": "Borrar y comprobar",
 "enunciado": "Borra la secuencia que acabas de crear. Completa el método y su código, que no devuelve cuerpo.",
 "pista1": "El método de borrar se llama igual que la acción en inglés. Su código es 'hecho, sin contenido'.",
 "pista2": "method delete y status 204.",
 "feature": r"""
  Scenario: Borrar una secuencia
    * def nombre = 'l07-' + java.util.UUID.randomUUID().toString().substring(0, 8)
    Given url sequenceUrl
    And path '/api/v1/sequences'
    And request { name: '#(nombre)' }
    When method post
    Then status 201
    Given path '/api/v1/sequences', nombre
    When method ⟦delete⟧
    Then status ⟦204⟧
    Given path '/api/v1/sequences', nombre
    When method get
    Then status 404
"""},
],
}

L08 = {
"code": "L08", "slug": "validar-sin-fragilidad", "url": "uuidUrl",
"readme": r"""
# L08 · Validar sin fragilidad: fuzzy matchers

**En esta lección** compruebas la **forma** de una respuesta cuando los valores cambian en cada llamada.

## La idea

Un UUID nuevo es distinto cada vez: no puedes escribir su valor esperado. Pero sí sabes **qué forma** tiene. Para eso Karate tiene los *fuzzy matchers*, marcadores que empiezan por `#`:

| Marcador | Acepta |
| --- | --- |
| `#string` `#number` `#boolean` | un valor de ese tipo |
| `#array` `#object` | una lista / un objeto |
| `#uuid` | un UUID válido |
| `#regex ...` | un texto que cumple la expresión regular |
| `#[3]` | una lista de exactamente 3 elementos |
| `#notnull` `#null` | cualquier cosa menos null / solo null |
| `#present` `#notpresent` | el campo existe / no existe |
| `#ignore` | cualquier cosa (no lo compruebes) |
| `##string` | un texto **o** null o ausente (opcional) |
| `#? _ > 0` | lo que cumpla la condición (`_` es el valor) |

## La norma

1. Los valores que cambian (ids, fechas, tokens) se validan por su forma, nunca por su valor.
2. Los valores que **no** cambian (estado, versión, totales conocidos) se validan por su valor.
3. Un esquema completo con `==` y marcadores es la prueba más fuerte: falla si sobra o falta un campo.

## El fundamento

Un test que compara un UUID con `==` pasa una vez y falla siempre después. Un test con `#ignore` en todo nunca falla. El arte está en el equilibrio: forma para lo variable, valor para lo fijo.

## Así se escribe

```gherkin
And match response == { version: 7, count: 3, uuids: '#[3]', generatedAt: '#string' }
And match each response.uuids == '#uuid'
And match response.count == '#? _ >= 1 && _ <= 1000'
```

## Tus ejercicios

`academia corregir L08`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L08. Ejecútalo con:  academia ejemplo L08
@ejemplo
Feature: L08 · Comprobar la forma, no el valor

  Scenario: Un lote de UUID v7
    Given url uuidUrl
    And path '/api/v1/uuids'
    And params { version: 7, count: 3 }
    When method get
    Then status 200
    # Esquema completo: lo fijo por valor, lo variable por forma
    And match response == { version: 7, count: 3, uuids: '#[3]', generatedAt: '#string' }
    And match each response.uuids == '#uuid'
    And match response.uuids[0] == '#regex [0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}'
    And match response.count == '#? _ >= 1 && _ <= 1000'
""",
"ejercicios": [
{"id": "E01", "title": "Un UUID sin saber su valor",
 "enunciado": "Pide un UUID y comprueba que lo es, sin saber su valor.",
 "pista1": "Hay un marcador con el mismo nombre que el tipo de dato.",
 "pista2": "'#uuid'",
 "feature": r"""
  Scenario: Es un UUID
    Given url uuidUrl
    And path '/api/v1/uuids'
    When method get
    Then status 200
    And match response.uuids[0] == ⟦'#uuid'⟧
"""},
{"id": "E02", "title": "Un esquema completo",
 "enunciado": "Completa el esquema de la respuesta: el último campo es una fecha que cambia en cada llamada.",
 "pista1": "La fecha llega como texto en JSON.",
 "pista2": "generatedAt: '#string'",
 "feature": r"""
  Scenario: Esquema de un lote
    Given url uuidUrl
    And path '/api/v1/uuids'
    And params { version: 7, count: 3 }
    When method get
    Then status 200
    And match response == { version: 7, count: 3, uuids: '#array', generatedAt: ⟦'#string'⟧ }
"""},
{"id": "E03", "title": "El tamaño de una lista",
 "enunciado": "Pedimos 3 UUID. Comprueba que la lista tiene exactamente 3 elementos con un solo marcador.",
 "pista1": "El marcador de tamaño usa corchetes con el número dentro.",
 "pista2": "'#[3]'",
 "feature": r"""
  Scenario: Tres elementos
    Given url uuidUrl
    And path '/api/v1/uuids'
    And param count = 3
    When method get
    Then status 200
    And match response.uuids == ⟦'#[3]'⟧
"""},
{"id": "E04", "title": "Todos los elementos",
 "enunciado": "Pedimos 50 UUID. Comprueba con match each que todos son UUID.",
 "pista1": "match each aplica la comprobación a cada elemento de la lista.",
 "pista2": "match each response.uuids == '#uuid'",
 "feature": r"""
  Scenario: Cincuenta UUID válidos
    Given url uuidUrl
    And path '/api/v1/uuids'
    And param count = 50
    When method get
    Then status 200
    And match each response.uuids == ⟦'#uuid'⟧
"""},
{"id": "E05", "title": "Una expresión regular",
 "enunciado": "Un UUID v7 tiene un 7 al principio del tercer bloque. Completa la expresión regular.",
 "pista1": "El tercer bloque empieza por la versión del UUID.",
 "pista2": "El hueco es 7.",
 "feature": r"""
  Scenario: Es un UUID de versión 7
    Given url uuidUrl
    And path '/api/v1/uuids/one'
    And param version = 7
    When method get
    Then status 200
    And match response.uuid == '#regex [0-9a-f]{8}-[0-9a-f]{4}-⟦7⟧[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}'
"""},
{"id": "E06", "title": "Campos opcionales",
 "enunciado": "Validamos un texto que no es un UUID: version llega como null. Usa el marcador de número opcional.",
 "pista1": "Un marcador con doble # acepta también null o ausente.",
 "pista2": "'##number'",
 "feature": r"""
  Scenario: Validar algo que no es un UUID
    Given url uuidUrl
    And path '/api/v1/uuids/validate'
    And request { value: 'no-soy-un-uuid' }
    When method post
    Then status 200
    And match response == { value: '#string', valid: false, version: ⟦'##number'⟧, variant: '##string', reason: '#string' }
"""},
],
}

L09 = {
"code": "L09", "slug": "listas-each-filter-map", "url": "catalogUrl",
"readme": r"""
# L09 · Listas: match each, karate.filter y karate.map

**En esta lección** trabajas con listas completas: comprobar todos sus elementos, filtrarlas y transformarlas.

## La idea

| Herramienta | Para qué | Ejemplo |
| --- | --- | --- |
| `match each lista contains {...}` | Que **cada** elemento cumpla algo | `match each response.items contains { category: 'EQUITY' }` |
| `karate.filter(lista, fn)` | Quedarte con los que cumplen una condición | `karate.filter(items, function(f){ return f.sri > 5 })` |
| `karate.map(lista, fn)` | Transformar cada elemento | `karate.map(items, function(f){ return f.name })` |
| `get lista[*].campo` | Sacar un campo de todos (JsonPath) | `* def isins = get response.items[*].isin` |
| `karate.sizeOf(lista)` | Contar | `karate.sizeOf(response.items)` |

## La norma

1. Si la regla de negocio dice "todos", el test usa `match each`.
2. Las condiciones van dentro de una función: `function(f){ return ... }` donde `f` es cada elemento.
3. Los importes llegan como texto: conviértelos con `parseFloat` antes de comparar con `<` o `>`.
4. En `match` puedes escribir `response.items[*].isin` directamente; en `def` hace falta `get` delante.

## El fundamento

Un filtro que devuelve 7 fondos con 6 correctos y 1 incorrecto pasa un test que solo mira el primero. `match each` es lo que garantiza que la regla se cumple para **todos** los datos, no solo para el ejemplo que elegiste.

## Así se escribe

```gherkin
* def caros = karate.filter(response.items, function(f){ return parseFloat(f.management_fee) > 1 })
* match caros == '#[4]'
* def nombres = karate.map(response.items, function(f){ return f.name })
* match nombres contains 'Nordvik Balanserad'
```

## Tus ejercicios

`academia corregir L09`.
""",
"ejemplo": r"""
# Ejemplo resuelto de L09. Ejecútalo con:  academia ejemplo L09
@ejemplo
Feature: L09 · Trabajar con listas

  Background:
    * url catalogUrl
    * path '/api/v1/funds'

  Scenario: Todos los fondos de renta variable tienen riesgo alto
    Given param category = 'EQUITY'
    When method get
    Then status 200
    And match each response.items contains { category: 'EQUITY' }
    And match each response.items contains { sri: '#? _ >= 4' }

  Scenario: Filtrar y transformar
    When method get
    Then status 200
    * def caros = karate.filter(response.items, function(f){ return parseFloat(f.management_fee) > 1 })
    * match caros == '#[4]'
    * def nombres = karate.map(caros, function(f){ return f.name })
    * match nombres contains 'Nordvik Tech Growth'
    * print 'Fondos con comisión mayor del 1 %:', nombres
""",
"ejercicios": [
{"id": "E01", "title": "Cada elemento cumple",
 "enunciado": "Filtramos por EQUITY. Comprueba que cada fondo devuelto es de esa categoría.",
 "pista1": "match each ... ___ { category: 'EQUITY' }: falta la palabra que permite que sobren campos.",
 "pista2": "contains",
 "feature": r"""
  Scenario: El filtro de categoría funciona
    Given url catalogUrl
    And path '/api/v1/funds'
    And param category = 'EQUITY'
    When method get
    Then status 200
    And match each response.items ⟦contains⟧ { category: 'EQUITY' }
"""},
{"id": "E02", "title": "Filtrar por una condición",
 "enunciado": "Quédate con los fondos cuya comisión de gestión es menor del 0,30 %. Son 4.",
 "pista1": "La condición compara con un número decimal escrito con punto.",
 "pista2": "0.30",
 "feature": r"""
  Scenario: Fondos baratos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def baratos = karate.filter(response.items, function(f){ return parseFloat(f.management_fee) < ⟦0.30⟧ })
    * match baratos == '#[4]'
"""},
{"id": "E03", "title": "Transformar cada elemento",
 "enunciado": "Construye la lista de nombres de los fondos.",
 "pista1": "f es cada fondo; devuelve el campo que contiene su nombre.",
 "pista2": "f.name",
 "feature": r"""
  Scenario: Lista de nombres
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def nombres = karate.map(response.items, function(f){ return f.⟦name⟧ })
    * match nombres contains 'Nordvik Balanserad'
    * match nombres == '#[14]'
"""},
{"id": "E04", "title": "Un campo de todos con JsonPath",
 "enunciado": "Saca la lista de ISIN de todos los fondos sin escribir una función.\nOjo: para guardar una expresión JsonPath en una variable, def necesita la palabra get.",
 "pista1": "Con [*] sacas un campo de todos los elementos de una lista; delante va la palabra get.",
 "pista2": "get response.items[*].isin",
 "feature": r"""
  Scenario: Todos los ISIN
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def isins = ⟦get response.items[*].isin⟧
    * match isins == '#[14]'
    * match each isins == '#regex [A-Z]{2}[0-9A-Z]{9}[0-9]'
"""},
{"id": "E05", "title": "Una condición en cada elemento",
 "enunciado": "El riesgo SRI va de 1 a 7. Comprueba que todos los fondos están en ese rango.",
 "pista1": "_ es el valor de sri de cada fondo. Falta el límite superior.",
 "pista2": "7",
 "feature": r"""
  Scenario: Riesgo dentro de la escala
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    And match each response.items contains { sri: '#? _ >= 1 && _ <= ⟦7⟧' }
"""},
{"id": "E06", "title": "Contar los de una divisa",
 "enunciado": "¿Cuántos fondos cotizan en euros? Filtra y comprueba que son 2.",
 "pista1": "Compara el campo base_currency con el código ISO del euro.",
 "pista2": "'EUR'",
 "feature": r"""
  Scenario: Fondos en euros
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
    * def enEuros = karate.filter(response.items, function(f){ return f.base_currency == ⟦'EUR'⟧ })
    * match enEuros == '#[2]'
"""},
],
}

LECCIONES = [L04, L05, L06, L07, L08, L09]
