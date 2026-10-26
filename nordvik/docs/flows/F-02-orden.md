# F-02 · Orden de compra y ejecución

```mermaid
sequenceDiagram
  participant A as Asesor / cliente
  participant OM as order-management 6902
  participant CM as cash-management 7004
  participant OR as order-router 6903
  participant CU as custodian-sim 7704
  participant CA as custody-adapter 7500
  participant EX as execution-service 6904
  participant PK as position-keeping 6905
  A->>OM: POST /api/v1/orders + Idempotency-Key
  OM->>CM: retener efectivo (hold)
  OM-->>A: 201 CREATED  (repetida: misma respuesta + Idempotent-Replayed: true)
  Note over OR: corte diario 15:30 (reloj virtual)
  OR->>CU: lote del día (POST /api/v1/batches/run lo fuerza)
  Note over CU: ejecuta a las 18:05 con el NAV del día
  CU->>CA: webhook de ejecución
  CA->>EX: custody.execution.received
  EX->>OM: trade.executed → orden EXECUTED
  EX->>PK: trade.executed → posiciones
```

## Reglas de negocio (códigos que verás)

| Caso | Respuesta |
|---|---|
| Sin `Idempotency-Key` | 400 `IDEMPOTENCY_KEY_REQUIRED` |
| Misma clave, otro cuerpo | 422 `IDEMPOTENCY_KEY_REUSED` |
| Importe < mínimo del fondo | 422 `BELOW_MINIMUM` |
| Sin efectivo | 422 `INSUFFICIENT_FUNDS` (y se guarda: repetirla da el mismo error) |
| Fondo cerrado / ISIN desconocido | 422 `FUND_CLOSED` / `UNKNOWN_FUND` |
| Cartera ajena (cliente) | 404 `PORTFOLIO_NOT_FOUND` |
| Cartera de un cliente no asignado (asesor) | 403 `ACCESS_DENIED` |
| Cancelar una orden ya enviada | 409 `ORDER_NOT_CANCELLABLE` |

## Dónde se prueba

`TradingTests.cs` (con `Flows.ExecuteAt1805`) y la academia `L19`.
