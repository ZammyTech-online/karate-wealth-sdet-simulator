# Pistas de L16

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L16 E0X`.

## E01 · Tu primer token

'cliente.ana'

## E02 · Una clave mala

400 (OAuth2 devuelve invalid_grant con 400). Míralo tú en Swagger: http://localhost:6200/swagger

## E03 · Manda el token

'Bearer ' + ana.token

## E04 · 401 sin token

401 y 'AUTHENTICATION_REQUIRED'

## E05 · 403: sé quién eres y no puedes

403 y 'ROLE_NOT_ALLOWED'

## E06 · Un solo login por feature

callonce
