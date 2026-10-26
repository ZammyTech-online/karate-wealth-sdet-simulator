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
