# L12 · El tiempo en tus tests: el reloj virtual

**En esta lección** controlas la hora de todo el sistema para probar reglas que dependen del tiempo, sin esperar.

## La idea

En finanzas casi todo depende de la hora: la hora de corte de las 15:00, la publicación del NAV a las 18:00, los festivos, los plazos. Esperar a las 15:00 para probar no es una opción.

En Nordvik **ningún servicio usa la hora del ordenador**: todos preguntan a `clock-service`. Y tú puedes moverlo:

| Petición | Efecto |
| --- | --- |
| `POST /api/v1/clock/freeze { "at": "2026-10-26T14:59:00+01:00" }` | Congela el reloj en ese instante |
| `POST /api/v1/clock/advance { "minutes": 2 }` | Avanza (o retrocede con negativos) |
| `POST /api/v1/clock/travel { "to": "..." }` | Salta a esa fecha y sigue corriendo |
| `POST /api/v1/clock/reset` | Vuelve a la hora real |

## La norma

1. Escribe siempre la hora con su zona: `+01:00` en invierno y `+02:00` en verano en Estocolmo.
2. **Todo test que toca el reloj lo deja como estaba.** Para eso, en el `Background`:
   `* configure afterScenario = function(){ karate.http(clockUrl + '/api/v1/clock/reset').post({}) }`
3. Prueba los bordes: 14:59:59 y 15:00:00, 17:59 y 18:00.

## El fundamento

Las reglas de tiempo son de las que más fallan en producción: el cambio de hora, el último día del mes, la orden que llega un segundo tarde. Un reloj controlable las convierte en tests deterministas que dan el mismo resultado hoy y dentro de un año.

## Tus ejercicios

`academia corregir L12`.
