# F-08 · Cobro de la pensión y fallecimiento

```mermaid
sequenceDiagram
  participant A as Asesor / cliente
  participant PL as payout-planner 7110
  participant PP as pension-plan-service 7100
  participant AC as annuity-calculator 7112
  participant VS as valuation-service 6906
  participant PE as payout-execution 7111
  participant LG as ledger-service 7003
  participant DC as death-claims 7113
  participant BS as beneficiary-service 7107
  A->>PL: POST /pension-plans/{id}/payout-plans {TERM, termYears}
  PL->>PP: elegibilidad (55 años, plan ACTIVE)
  PL->>VS: valor del plan (capital)
  PL->>AC: renta mensual (sexo por número nacional) → DRAFT
  A->>PL: /activate → ACTIVE (el plan pasa a PAYOUT)
  Note over PE: cada mes (Operaciones o Service)
  PE->>PL: planes de cobro ACTIVE
  PE->>LG: asiento bruto / retención 30 % / neto  · evento payout.paid
  Note over DC: Operaciones abre una reclamación por fallecimiento
  DC->>PP: todos los planes → FROZEN
  DC->>PL: cobros → SUSPENDED
  DC->>BS: verificar: reparto por porcentajes (sin beneficiarios → ESTATE)
  DC->>PP: liquidar: planes → CLOSED; cobros → FINISHED
```

| Caso | Respuesta |
|---|---|
| Menor de 55 años | 422 `NOT_ELIGIBLE` |
| Plan sin dinero invertido | 422 `NOTHING_TO_PAY` |
| Segundo plan de cobro vivo | 409 `PAYOUT_PLAN_EXISTS` |
| Activar dos veces / suspender un DRAFT | 409 `INVALID_STATE_TRANSITION` |
| Pagar un mes futuro | 422 `FUTURE_PERIOD` |
| Repetir la ejecución del mes | 200 con `ALREADY_PAID` (no paga dos veces) |
| Aportar a un plan congelado / en cobro | 409 `PLAN_FROZEN` / `PLAN_NOT_ACTIVE` |
| Reanudar el cobro con el plan congelado | 409 `PLAN_FROZEN` |
| Segunda reclamación del mismo cliente | 409 `CLAIM_ALREADY_OPEN` |
| Fecha de fallecimiento futura | 422 `DATE_IN_FUTURE` |

La retención del 30 % es provisional: en la ola 4 la calculará tax-withholding. La parte de seguro de vida queda `PENDING_WAVE_4`.

## Dónde se prueba

`PayoutTests` y `DeathClaimTests` (PensionTests.cs).
