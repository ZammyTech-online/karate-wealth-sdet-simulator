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
