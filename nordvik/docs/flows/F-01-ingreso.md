# F-01 · Ingreso de dinero

```mermaid
sequenceDiagram
  participant T as Test / Operaciones
  participant PG as payments-gateway 7000
  participant NP as nordpay-sim 7706 (banco)
  participant AD as nordpay-adapter 7502
  participant BUS as event-bus 7400
  participant L as ledger 7003 · cash 7004
  T->>PG: POST /api/v1/deposits {customerId, expectedAmount}
  PG-->>T: 201 AWAITING_FUNDS + reference (OCR) + toIban
  T->>NP: POST /__admin/incoming-credits {debtorIban, amount, reference}
  NP-->>T: 201 BOOKED
  NP->>AD: webhook camt.054 (firmado con HMAC)
  AD->>BUS: payment.credit.received
  BUS->>PG: (consumidor) casa por OCR
  PG->>L: asiento contable + efectivo disponible
  PG->>BUS: deposit.received → notification-service, audit-service
  T->>PG: GET /api/v1/payments/{id}  (retry until status == RECEIVED)
```

## Reglas

- La referencia OCR identifica al cliente: sin ella el abono queda **sin casar** y lo resuelve `reconciliation-service`.
- Importe distinto al esperado → se acepta el real y `amountMismatch: true`.
- El ingreso caduca a los 7 días (`expiresAt`).
- Para probar caídas del banco: `POST /__admin/behaviour` en nordpay-sim (`down`, `slow`, `failNext`).

## Dónde se prueba

`tests/Nordvik.ApiTests/PaymentsTests.cs`, el ayudante `Flows.Deposit` y en la academia `L18` y `_cliente-con-dinero.feature`.
