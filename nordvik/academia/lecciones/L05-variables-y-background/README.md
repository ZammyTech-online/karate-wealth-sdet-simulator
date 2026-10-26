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
