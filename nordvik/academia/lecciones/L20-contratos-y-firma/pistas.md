# Pistas de L20

Pista 1 está en cada ejercicio. Aquí tienes la pista 2. La pista 3 es la solución: `academia solucion L20 E0X`.

## E01 · Nace un borrador

'MASTER_AGREEMENT' y 'DRAFT'

## E02 · Enviar a firma

'send' y 'PENDING_SIGNATURE'

## E03 · Lo que se firma es el hash

'CUSTOMER' y contrato.contentHash

## E04 · Esperar a NordID

response.status != 'PENDING'

## E05 · Un contrato ajeno

404 y 'CONTRACT_NOT_FOUND'

## E06 · Las cláusulas vienen ordenadas

match each y content.clauses[0].code
