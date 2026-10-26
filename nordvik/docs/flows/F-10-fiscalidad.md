# F-10 · Retenciones, informe anual y certificados (E8)

```mermaid
sequenceDiagram
  participant PE as payout-execution 7111
  participant TW as tax-withholding 7300
  participant TR as tax-residency-service 6308
  participant TA as tax-agency-adapter 7507
  participant TS as tax-agency-sim 7708
  participant RP as tax-reporting 7301
  participant AS as annual-statement 7302
  PE->>TW: ¿cuánto retengo a este cliente?
  TW->>TR: residencia fiscal principal
  alt no reside en Suecia
    TW-->>PE: SINK 25 %
  else
    TW->>TA: tabla de retención (cacheada por persona y año)
    TA->>TS: GET /v1/tax-tables/{pnr} + X-Api-Key
    TW-->>PE: TAX_TABLE (municipio y tipo)
  end
  Note over RP: Operaciones, una vez al año
  RP->>PE: pagos del año por cliente
  RP->>TA: fichero de control ORIGINAL (o CORRECTION)
  TS-->>RP: acuse con aceptadas y rechazadas
  AS->>RP: línea del cliente → certificado JSON y PDF
```

| Caso | Respuesta |
|---|---|
| Rentas del capital (INTEREST, DIVIDEND, CAPITAL_GAIN) | 30 % plano (`CAPITAL_INCOME`) |
| Segundo ORIGINAL del mismo año | 409 `REPORT_ALREADY_FILED` (usa `CORRECTION`) |
| CORRECTION sin original | 409 `NOTHING_TO_CORRECT` |
| La Agencia rechaza el fichero entero (`/__admin/reject-next`) | 422 con su código (`SCHEMA_ERROR`) |
| Certificado de un año sin presentar | 404 `STATEMENT_NOT_AVAILABLE` |
| Extracto de un periodo que no ha empezado | 422 `PERIOD_NOT_STARTED` |
| Analítica con k < 5 | 400 `K_TOO_LOW`; los grupos pequeños salen `suppressed: true` con null |

## Dónde se prueba

`TaxTests` (InsuranceTaxTests.cs) y `PayoutTests`.
