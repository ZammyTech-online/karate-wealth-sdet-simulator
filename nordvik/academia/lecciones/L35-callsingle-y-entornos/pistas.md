# Pistas de L35

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L35 E0X`.

## E01 · Invocar rutina con karate.callSingle

match res.token == 'nordvik-bearer-cache-token-xyz'

## E02 · Validar propiedad de rol administrativo

match auth.role == 'SYSTEM_ADMIN'

## E03 · Comprobar persistencia idéntica en memoria

No vuelve a ejecutar la red, lee del mapa de memoria

## E04 · Inyección en cabeceras de petición

header Authorization = 'Bearer ' + auth.token

## E05 · Configuración multientorno con karate.env

match env == '#string'
