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
