# F-09 · Seguro de vida ligado a la pensión (E11)

```mermaid
sequenceDiagram
  participant A as Asesor / cliente
  participant HD as health-declaration 7201
  participant IA as insurer-adapter 7508
  participant IS as insurer-sim 7711
  participant IP as insurance-policy 7200
  participant PC as premium-calculator 7202
  participant IC as insurance-claims 7203
  participant O as Operaciones
  A->>HD: cuestionario + consentToProcessHealthData: true
  IA->>IS: POST /oauth/token (client_credentials) · token cacheado
  HD->>IA: suscripción médica → STANDARD / RATED (+25/50/75 %) / DECLINED
  Note over HD: solo se guarda el resultado; caduca a los 90 días
  A->>IP: póliza LIFE o DISABILITY sobre un plan ACTIVE
  IP->>PC: prima con el recargo médico
  IP->>IA: emitir póliza → INS-xxxxxxx
  O->>IC: siniestro DEATH (LIFE) o DISABILITY
  IC->>IA: abrir siniestro en la aseguradora → SUBMITTED
  Note over IS: /__admin/claims/{id}/decide
  IC->>IC: GET sincroniza: APPROVED → PAID (asiento) y póliza CLAIMED
```

| Caso | Respuesta |
|---|---|
| Cuestionario sin consentimiento explícito | 422 `EXPLICIT_CONSENT_REQUIRED` |
| Declaración caducada / de otra persona | 422 `HEALTH_DECLARATION_EXPIRED` / `DECLARATION_NOT_FOUND` |
| La aseguradora no acepta el riesgo | 422 `APPLICANT_DECLINED` |
| Segunda póliza de la misma cobertura en el plan | 409 `POLICY_ALREADY_EXISTS` |
| Siniestro DEATH sobre una póliza DISABILITY | 422 `COVERAGE_MISMATCH` |
| Segundo siniestro abierto | 409 `CLAIM_ALREADY_OPEN` |
| La aseguradora invalida los tokens (`/__admin/tokens/expire-all`) | el adaptador renueva una vez y sigue (`/api/v1/token-status`) |

Si el cliente fallece (F-08), `death-claims` lista sus pólizas LIFE en `insurancePart` (`CLAIM_REQUIRED`).

## Dónde se prueba

`InsuranceTests` (InsuranceTaxTests.cs).
