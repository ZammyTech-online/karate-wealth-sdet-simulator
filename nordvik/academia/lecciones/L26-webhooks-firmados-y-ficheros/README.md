# L26 · Webhooks firmados y ficheros de empresas (pensiones)

## La idea

Cada mes, el **Clearing de Empleadores** (simulador E9, 7709) junta las aportaciones de una empresa en un CSV y avisa a
Nordvik con un **webhook**. ¿Cómo sabe Nordvik que el aviso es auténtico? Por la cabecera
`X-Signature: sha256=<HMAC-SHA256 del cuerpo EXACTO con un secreto compartido>`.

En Karate firmas con Java desde JavaScript (el ayudante `_hmac.js` lo hace por ti):

```gherkin
* def hmac = read('_hmac.js')
* text cuerpo =
  """
  {"eventType":"FILE_AVAILABLE","fileId":"cf_x","employerOrgNumber":"556036-0793","period":"2026-10"}
  """
Given url clearingAdapterUrl
And path '/api/v1/webhooks/clearing'
And header X-Signature = 'sha256=' + hmac('e9-clearing-webhook-2026', cuerpo)
And request cuerpo
When method post
```

**Ojo:** `text` guarda el cuerpo como texto tal cual. Si usas `def`, Karate lo convierte en JSON y al enviarlo puede
cambiar espacios u orden: la firma dejaría de cuadrar (es la trampa T18).

El flujo completo (F-06): la empresa da de alta a sus empleados en `employer-portal-api` (7102) con su
`X-Employer-Key`; el clearing publica el fichero; Nordvik valida **línea a línea** y devuelve un acuse con
`accepted` y `rejected[]` (cada rechazo con su `code`), sin números nacionales en claro.

## La norma

1. Firma **el texto exacto** que envías. Nada de reconstruir el JSON.
2. Prueba tres firmas: ninguna, una con otro secreto y la buena.
3. En los ficheros, prueba cada código de rechazo con una línea dedicada.
