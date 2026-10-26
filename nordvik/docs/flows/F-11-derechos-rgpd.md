# F-11 · Derechos RGPD: borrado, acceso y brechas

```mermaid
sequenceDiagram
  participant C as Cumplimiento / cliente
  participant ER as erasure-orchestrator 6507
  participant KM as key-management 6501
  participant PV as pii-vault 6500
  participant CL as consent-ledger 6502
  participant CR as customer-registry 6300
  participant DS as dsar-service 6508
  participant BR as breach-service 6509
  participant RR as regulatory-reporting 6408
  C->>ER: borrado (art. 17)
  ER->>ER: bloqueos: planes de pensión vivos, carteras con dinero, seguros
  ER->>KM: triturar claves customer:{id}
  KM-->>PV: evento key.shredded → tokens ERASED
  ER->>CL: retirar consentimientos
  ER->>CR: cerrar la ficha
  ER-->>C: COMPLETED + certificado SHA-256 + lo que se retiene por ley
  C->>DS: acceso (art. 15) o portabilidad (art. 20) → paquete (3 descargas, 7 días)
  C->>BR: brecha → 72 h para la autoridad; si riesgo HIGH, aviso a cada afectado
  C->>RR: informe mensual de incidentes al supervisor
```

| Caso | Respuesta |
|---|---|
| Borrado con contratos vivos | 201 `BLOCKED` con `blockers` (no se toca nada) |
| Segundo borrado del mismo cliente | 409 `ALREADY_ERASED` |
| Cuarta descarga del paquete / pasados 7 días | 410 `DOWNLOAD_LIMIT_REACHED` / `PACKAGE_EXPIRED` |
| Avisar a los afectados antes que a la autoridad | 409 `AUTHORITY_FIRST` |
| Riesgo LOW y aviso a afectados | 422 `SUBJECT_NOTICE_NOT_REQUIRED` |
| Aviso a la autoridad pasadas 72 h | se acepta, pero `authorityNoticeLate: true` para siempre |

## Dónde se prueba

`DataRightsTests` y `GovernanceTests` (EnterpriseTests.cs).
