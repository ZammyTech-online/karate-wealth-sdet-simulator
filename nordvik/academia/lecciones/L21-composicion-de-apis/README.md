# L21 · Dos APIs, un JSON: probar una composición

## La idea

`composition-service` (6107) **une respuestas de varios servicios** en un solo JSON. La composición C1,
*contract-bundle*, junta el contrato (`contract-service`) con su anexo (`document-vault`):

```json
{
  "status": "COMPLETE",
  "bundle": { "contract": { ... }, "annex": { ... } },
  "sources": [ { "service": "contract-service", "version": 2, "status": 200, "ms": 6, "fetchedAt": "..." }, { "service": "document-vault", ... } ],
  "missing": []
}
```

Si falta una parte (el contrato no tiene anexo), la composición **no falla**: responde `PARTIAL` y lo dice en `missing`.

## Qué se prueba de una composición

1. **Que es la unión exacta**: `bundle.contract` es *igual* a lo que devuelve el servicio de contratos. Con `match ==` sobre
   objetos enteros, Karate compara campo a campo y te dice exactamente cuál difiere.
2. **Que es honesta cuando falta algo**: `PARTIAL` + `missing`.
3. **Que respeta la seguridad de cada fuente**: Lars no ve el bundle de Ana (404), y sin token es 401.
4. **La trazabilidad**: con la cabecera `X-Composition-Trace: true` añade un bloque `trace`; sin ella no aparece.

## La norma

1. Compara la composición contra **las fuentes reales**, llamadas en el mismo test, nunca contra un JSON copiado.
2. `'#notpresent'` comprueba que un campo **no existe**; `'#present'` que existe aunque sea null.

## Tus ejercicios

`academia corregir L21`.
