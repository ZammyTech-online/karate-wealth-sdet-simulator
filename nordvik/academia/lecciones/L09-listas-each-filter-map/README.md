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
