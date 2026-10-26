# L30 · OAuth2 entre máquinas: la aseguradora

## La idea

La aseguradora (simulador E11, 7711) no conoce a tus usuarios: autentica **sistemas**. Nordvik pide un token con el
flujo **client credentials** (sin usuario, con `client_id` y `client_secret`) y lo manda como `Bearer`:

```gherkin
Given url insurerSimUrl
And path '/oauth/token'
And form field grant_type = 'client_credentials'
And form field client_id = 'nordvik'
And form field client_secret = 'e11-insurer-client-secret-2026'
When method post
Then status 200
* def token = response.access_token
```

El token **caduca** (300 s en el reloj virtual) y la aseguradora puede invalidarlos todos de golpe
(`POST /__admin/tokens/expire-all`). Un cliente bien hecho pide uno nuevo y repite **una sola vez**; es lo que hace
`insurer-adapter` (7508), y lo puedes ver en `GET /api/v1/token-status`.

Con el token pides la **suscripción médica**: `POST /v1/underwriting` con edad, fumador, IMC y enfermedades →
STANDARD, RATED (recargo del 25/50/75 %) o DECLINED. La aseguradora **no guarda las respuestas** (`answersRetained: false`).

## La norma

1. Los errores de OAuth2 no son *problems* de Nordvik: vienen como `{ "error": "invalid_client" }`.
2. Prueba que tu cliente sobrevive a un token caducado: invalídalos y repite.
3. El secreto nunca va en el código del test: en una empresa real sale de un almacén de secretos (aquí, `secrets-service`).
