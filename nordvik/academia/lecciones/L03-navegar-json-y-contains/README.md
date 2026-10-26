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
