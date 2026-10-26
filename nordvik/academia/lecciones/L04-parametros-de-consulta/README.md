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
