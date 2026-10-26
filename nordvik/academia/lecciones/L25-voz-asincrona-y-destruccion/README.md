# L25 · Llamadas de voz: procesos asíncronos que se autodestruyen

## La idea

Una llamada con un asesor pasa por un **pipeline** que no termina cuando tu petición responde:

```
POST /api/v2/calls  (201)  →  play-script (202)  →  end (202)
   … transcripción → redacción de datos personales → extracción de datos → resumen …
   →  status AWAITING_REVIEW  o  SHREDDED (audio y transcripción destruidos, recibo firmado)
```

Un **202 Accepted** significa "lo he recibido, lo haré": tu test tiene que **esperar** con `retry until`.

```gherkin
* configure retry = { count: 30, interval: 500 }
Given url voiceUrl
And path '/api/v2/calls', id
And headers erik.auth
And retry until response.status == 'SHREDDED'
When method get
Then status 200
```

Reglas de Nordvik:
- Sin el consentimiento `VOICE_RECORDING` no hay llamada (409 `RECORDING_CONSENT_MISSING`).
- Un número nacional dictado en la llamada **nunca** aparece en la transcripción: sale `[NATIONAL_ID]`.
- Al destruir, Nordvik emite un **recibo firmado**: `POST /api/v2/receipts/verify` dice si es auténtico.

El simulador de telefonía (E12) tiene guiones: `v2-consulta-corta` (se resume y se destruye) y
`v3-dicta-numero` (el cliente dicta su número; queda en revisión).

## La norma

1. Tras un 202, **nunca** compruebes el resultado sin esperar.
2. Pon un `count` y un `interval` razonables: que el test falle claro si el proceso se cuelga.
3. Después de destruir, comprueba que lo destruido **ya no se puede pedir** (404).
