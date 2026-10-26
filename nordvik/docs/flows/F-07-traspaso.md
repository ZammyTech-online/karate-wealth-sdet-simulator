# F-07 · Traspaso entrante desde otra gestora (E10)

```mermaid
sequenceDiagram
  participant A as Asesor / cliente
  participant TS as transfer-service 7108
  participant TV as transfer-valuation 7109
  participant PA as provider-network-adapter 7506
  participant PS as provider-network-sim 7710
  participant O as Operaciones
  participant CB as contribution-service 7101
  A->>TS: POST /api/v1/transfers (plan destino, gestora, póliza)
  TS->>TV: pedir valoración
  TV->>PA: firma la petición (HMAC) …
  PA->>PS: POST /v1/valuations → 202 PENDING, readyAt = +1..3 días VIRTUALES
  Note over TS: VALUATION_PENDING
  A->>TS: GET (cuando el reloj pasa readyAt) → VALUED
  PA->>PA: … y verifica la firma de cada respuesta (firma mutua)
  A->>TS: POST /confirm → AWAITING_APPROVAL
  O->>TS: POST /approve
  TS->>PA: POST /v1/transfers → importe liberado
  TS->>TV: comparar con la valoración (±2 %)
  alt dentro del 2 %
    TS->>CB: aportación TRANSFER → COMPLETED (evento transfer.completed)
  else fuera del 2 %
    Note over TS: MANUAL_REVIEW → accept-amount (COMPLETED) o reject (REJECTED)
  end
```

## Máquina de estados

`VALUATION_PENDING → VALUED → AWAITING_APPROVAL → COMPLETED | MANUAL_REVIEW → COMPLETED | REJECTED`;
cancelable por el cliente hasta la aprobación; `EXPIRED` si la valoración pasa de 30 días.

| Caso | Respuesta |
|---|---|
| Paso fuera de orden (confirmar sin valorar, cancelar tras completar…) | 409 `INVALID_STATE_TRANSITION` |
| Aprobar con la valoración caducada | 409 `VALUATION_EXPIRED` |
| Otra solicitud para la misma póliza en curso | 409 `TRANSFER_ALREADY_IN_PROGRESS` |
| Póliza o gestora desconocida | 404 `POLICY_NOT_FOUND` / `PROVIDER_NOT_FOUND` |
| La póliza es de otra persona | 422 `POLICY_HOLDER_MISMATCH` |
| La gestora firma mal su respuesta | 502 `INVALID_PROVIDER_SIGNATURE` |
| Aprobar sin ser Operaciones | 403 `ROLE_NOT_ALLOWED` |

`/__admin/policies` del simulador fija valor, días de demora y desviación; `/__admin/tamper` hace que firme mal.

## Dónde se prueba

`TransferTests` (PensionTests.cs) y `PensionMathTests` (tolerancia y estados).
