# F-04 · Composición contract-bundle (C1)

`GET /api/v2/compositions/contract-bundle/{contractId}` en composition-service (6107) devuelve:

```json
{
  "status": "COMPLETE | PARTIAL",
  "bundle": { "contract": { "...": "igual que GET /api/v2/contracts/{id}" }, "annex": { "...": "igual que GET document-vault /api/v1/documents/{docId}" } },
  "sources": [ { "service": "contract-service", "version": 2, "status": 200, "ms": 6, "fetchedAt": "..." }, { "service": "document-vault", "...": "..." } ],
  "missing": [],
  "note": "..."
}
```

## Contrato de la composición

1. `bundle.contract` y `bundle.annex` son **byte a byte** (JSON canónico) lo que devuelven sus fuentes.
2. Si falta el anexo: `PARTIAL` + `missing: ["annex"]`, nunca un 500.
3. La seguridad es la de las fuentes: otro cliente → 404 `CONTRACT_NOT_FOUND`; sin token → 401.
4. `X-Composition-Trace: true` añade `trace` con lo que respondió cada fuente; sin la cabecera, `trace` no existe.

Las composiciones C2-C6 siguen el mismo patrón; C7-C10 responden 501 hasta la oleada 3.

## Dónde se prueba

`CompositionTests.cs` y la academia `L21`.
