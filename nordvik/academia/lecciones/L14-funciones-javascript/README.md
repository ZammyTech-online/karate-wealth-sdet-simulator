# L14 · Funciones JavaScript propias

**En esta lección** escribes pequeñas funciones para calcular y comprobar cosas que Karate no trae de serie.

## La idea

Karate entiende JavaScript. Una función se guarda en una variable y se usa como cualquier otra:

```gherkin
* def doble = function(x){ return x * 2 }
* match doble(21) == 42
```

También puede vivir en un fichero `.js` (que contiene **solo** la función) y leerse con `read`:

```gherkin
* def formatear = read('formatearSek.js')
* match formatear(1500) == '1500.00 SEK'
```

Y puede usarse dentro de un fuzzy matcher: `'#? esPositivo(_)'`.

Utilidades de Karate muy usadas: `karate.repeat(n, fn)`, `karate.filter`, `karate.map`, `karate.sizeOf`.

## La norma

1. Una función, una responsabilidad, un nombre que diga lo que hace.
2. Las funciones que usan varios tests van en fichero `.js`, no copiadas.
3. Cuidado con los decimales: `toFixed(2)` redondea distinto al redondeo bancario del servidor en los casos de mitad exacta (lo verás en el nivel 7).

## El fundamento

El servidor calcula; tu test **recalcula de forma independiente** y compara. Si los dos llegan al mismo número por caminos distintos, el cálculo es fiable. Si copiaras el valor de la respuesta, no estarías probando nada.

## Tus ejercicios

`academia corregir L14`.
