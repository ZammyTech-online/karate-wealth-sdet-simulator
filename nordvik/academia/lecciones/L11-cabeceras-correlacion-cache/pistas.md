# Pistas de L11

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L11 E0X`.

## E01 · Envía tu correlationId

header X-Correlation-Id = 'mi-primer-id'

## E02 · Leer una cabecera de la respuesta

responseHeaders['ETag'][0]

## E03 · Dámelo solo si cambió

header If-None-Match = etag y status 304.

## E04 · ¿Quién me ha contestado?

'fx-rates'

## E05 · El tipo de contenido de un error

'application/problem+json'
