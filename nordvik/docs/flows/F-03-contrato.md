# F-03 · Contrato y firma electrónica

```
DRAFT ──send──▶ PENDING_SIGNATURE ──firma──▶ (PARTIALLY_SIGNED) ──▶ ACTIVE ──▶ WITHDRAWN · SUPERSEDED · TERMINATED
                         └──▶ EXPIRED · INVALID_SIGNATURE
```

```mermaid
sequenceDiagram
  participant E as Asesor
  participant C as contract-service 6700 (/api/v2)
  participant TPL as templates 6701 · clauses 6702
  participant S as signing-service 6703
  participant EID as eid-broker 6203
  participant N as nordid-sim 7701
  participant V as document-vault 6704
  E->>C: POST /contracts {type, customerId, accountType}
  C->>TPL: plantilla vigente + cláusulas según cuenta e idioma
  C-->>E: 201 DRAFT + contentHash (SHA-256 canónico)
  E->>C: POST /contracts/{id}/send → PENDING_SIGNATURE (contract.sent → email)
  Note over C: el cliente abre la firma
  C->>S: sesión de firma {party, signedHash = contentHash}
  S->>EID: orden de firma → N (outstandingTransaction → userSign → complete)
  C->>C: todas las partes firmadas → ACTIVE (contract.activated)
  C->>V: PDF firmado al vault
```

## Reglas

- NordID admite **una firma en curso por persona**: otra sesión mientras la primera está PENDING → 409 `SIGNING_ALREADY_IN_PROGRESS`.
- Los anexos solo se añaden en DRAFT (después: 409).
- Si lo firmado no coincide con el contrato (otro `contentHash` u otra versión de plantilla), el contrato pasa a `INVALID_SIGNATURE` (test T26).
- Plazo para firmar: 14 días desde el envío; después, `EXPIRED`.
- Desistimiento: 14 días desde la activación (`withdrawalDeadline`).

## Dónde se prueba

`ContractTests.cs` (`ContractFlows`) y la academia `L20`.
