# F-06 · Fichero mensual de un empleador (E9)

```mermaid
sequenceDiagram
  participant EP as employer-portal-api 7102
  participant CS as employer-clearing-sim 7709
  participant CA as employer-clearing-adapter 7505
  participant SS as secrets-service 6109
  participant FI as employer-file-ingest 7103
  participant CR as customer-registry 6300
  participant PP as pension-plan-service 7100
  participant CB as contribution-service 7101
  EP->>PP: alta de empleado → plan OCCUPATIONAL con el convenio de la empresa
  CS->>CA: webhook FILE_AVAILABLE + X-Signature (HMAC del cuerpo EXACTO)
  CA->>SS: secreto employer-clearing-hmac
  CA->>CS: GET /v1/files/{id} (CSV personnummer;belopp;avtal;period)
  CA->>FI: POST /api/v1/employer-files (token Service)
  loop cada línea
    FI->>CR: by-national-id
    FI->>PP: planes OCCUPATIONAL del cliente con esa empresa
    FI->>CB: aportación EMPLOYER (ledger + órdenes de compra)
  end
  FI-->>CA: acuse {accepted, acceptedAmount, rejected[]}  · evento employer-file.processed
  CA->>CS: POST /v1/files/{id}/receipt
```

## Reglas de negocio (códigos de rechazo por línea)

| Caso | Código |
|---|---|
| Línea con menos de 3 columnas | `MALFORMED_LINE` |
| Número nacional con dígito de control malo | `INVALID_PERSONAL_NUMBER` |
| Importe no numérico / ≤ 0 | `INVALID_AMOUNT` / `NEGATIVE_AMOUNT` |
| No es cliente de Nordvik | `UNKNOWN_EMPLOYEE` |
| Es cliente pero no tiene plan con esa empresa | `NO_PLAN_WITH_EMPLOYER` |
| El convenio del fichero no es el del plan | `AGREEMENT_MISMATCH` |
| Mismo empleado y mes (en el fichero o en uno anterior) | `DUPLICATE_MONTH` |
| Webhook sin firma o con firma de otro cuerpo | 401 `INVALID_SIGNATURE` |
| Mismo `fileId` otra vez | 200 con el resultado guardado (idempotente) |
| Portal sin `X-Employer-Key` válida | 401 `EMPLOYER_KEY_REQUIRED` |

El acuse nunca lleva el número nacional: solo `personalNumberMasked`.

## Dónde se prueba

`EmployerPensionTests` (PensionTests.cs).
