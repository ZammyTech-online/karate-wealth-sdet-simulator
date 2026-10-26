# L11 · Cabeceras, correlación y caché

**En esta lección** envías y lees cabeceras HTTP, sigues una petición con su identificador y pruebas la caché.

## La idea

Las cabeceras son datos **sobre** la petición o la respuesta. Las que más usarás en Nordvik:

| Cabecera | Quién la envía | Para qué |
| --- | --- | --- |
| `X-Correlation-Id` | tú (o el servidor si no la envías) | Seguir una petición por todos los servicios y logs |
| `X-Service`, `X-Service-Version` | el servidor | Saber quién te contestó |
| `Content-Type` | los dos | El formato del cuerpo (`application/json`, `application/problem+json`) |
| `ETag` | el servidor | La "versión" de un recurso |
| `If-None-Match` | tú | "Dámelo solo si cambió": si no, el servidor responde `304` sin cuerpo |

En Karate: `header Nombre = valor` para enviar, y `responseHeaders['Nombre'][0]` para leer (es una lista porque una cabecera puede repetirse).

## La norma

1. Envía siempre tu propio `X-Correlation-Id` en los tests: cuando algo falle, lo buscarás en los logs.
2. Un `304` no tiene cuerpo: no compruebes `response`.
3. Los nombres de cabecera se escriben como los envía el servidor (`ETag`, no `etag`).

## El fundamento

En un sistema con 156 servicios, el `correlationId` es la única forma de reconstruir qué pasó con una petición. Y la caché es una fuente clásica de bugs: datos viejos que nadie ve porque el código es `200`.

## Tus ejercicios

`academia corregir L11`.
