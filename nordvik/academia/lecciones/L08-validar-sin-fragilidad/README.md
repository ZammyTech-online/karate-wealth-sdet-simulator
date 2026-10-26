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
