# L15 · Proyecto del tramo: una suite de mercado completa

**En este proyecto** juntas todo lo aprendido en las lecciones L00-L14 en una suite real sobre los 8 servicios.

## El encargo

El equipo de Inversiones va a publicar una web de fondos. Antes, quiere una suite de regresión que garantice:

1. Que el catálogo devuelve los 14 fondos y que todos tienen una forma válida.
2. Que cada fondo abierto tiene precio publicado y que su divisa coincide entre catálogo y precios.
3. Que los festivos suecos no tienen precio.
4. Que la conversión de divisas cuadra con el tipo publicado.
5. Que la hora de corte se respeta (con el reloj virtual).
6. Que los errores tienen el formato RFC 9457.

## Cómo trabajarlo

- El fichero `ejercicios/E01.feature` tiene los 6 escenarios con huecos `___` más grandes que en las lecciones: a veces falta una línea entera.
- Puedes usar todo: `Background`, `call`, `Scenario Outline`, funciones JS, fuzzy matchers, reloj virtual.
- Cuando pase, ejecútalo varias veces: un buen test pasa siempre, no solo una vez.

## Criterios de calidad (los que usará un revisor)

- [ ] Ningún valor variable comprobado por valor (ids, fechas de hoy, NAV).
- [ ] Cada escenario se puede ejecutar solo y en cualquier orden.
- [ ] El reloj queda como estaba.
- [ ] Los nombres de escenario dicen la regla de negocio, no la técnica.

## Siguiente paso

Al terminar el proyecto has completado el **tramo 1** de la ruta (niveles 0-5 sobre la oleada 0). El siguiente tramo arranca con la oleada 1: autenticación, clientes y carteras.
