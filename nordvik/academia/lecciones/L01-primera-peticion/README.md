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
