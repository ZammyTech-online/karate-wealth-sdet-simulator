# Pistas de L34

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L34 E0X`.

## E01 · Arrancar un mock en puerto dinámico

Usa match mock.port == '#number' y luego mock.stop()

## E02 · Consultar el endpoint GET del mock

path '/api/v1/mock/actuary' y method get

## E03 · Enviar POST al mock y verificar respuesta 201

Valida response.result == 'COMMITTED'

## E04 · Detener el mock y liberar el puerto

El método stop() no recibe argumentos

## E05 · Aislamiento de tests con mock independiente

Cada uno obtiene un puerto asignado
