# L13 · Reutilizar: call y features llamables

**En esta lección** escribes un paso una sola vez y lo llamas desde muchos tests.

## La idea

Cuando varios tests necesitan lo mismo (crear un cliente, pedir un token, crear una secuencia), se escribe en una feature aparte y se **llama**:

```gherkin
* def resultado = call read('_crear-secuencia.feature') { prefix: 'CLI' }
* match resultado.response.prefix == 'CLI'
* def nombre = resultado.nombre
```

- Lo que pasas entre llaves llega a la feature llamada como **variables** (`prefix`).
- Lo que devuelve `call` es un objeto con **todas** las variables de la feature llamada (`nombre`, `response`...).
- `callonce` hace lo mismo pero solo la primera vez en ese fichero; las siguientes reutiliza el resultado.
- Si pasas una **lista** de objetos, `call` se ejecuta una vez por elemento y devuelve una lista.

## La norma

1. Las features que solo sirven para ser llamadas llevan el tag `@ignore` para que no se ejecuten solas.
2. Una feature llamable hace **una** cosa y la devuelve; no comprueba reglas del test que la llama.
3. `callonce` solo para cosas caras que no cambian (un token); nunca para datos que el test modifica.

## El fundamento

Si 40 tests crean clientes copiando el mismo bloque, un cambio en la API te obliga a tocar 40 ficheros. Con una feature llamable tocas uno. Es la primera capa del framework de 5 capas del plan.

## Tus ejercicios

El ayudante `_crear-secuencia.feature` ya está en la carpeta de ejercicios. `academia corregir L13`.
