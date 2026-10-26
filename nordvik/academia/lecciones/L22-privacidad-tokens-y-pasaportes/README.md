# L22 · Privacidad: tokens, máscaras y pasaportes de un solo uso

**Tramo 3.** Desde aquí trabajas con la oleada 3 entera. Arranca el perfil grande:

```
nordvik up -Profile nivel-9
```

## La idea

En Nordvik un dato personal (número nacional, IBAN, correo, teléfono) **casi nunca viaja en claro**. Se cambia por un
**token** en `pii-vault` (6500) y lo que circula es el token y una **máscara**:

```gherkin
Given url piiVaultUrl
And path '/api/v1/tokens'
And headers karin.auth
And request { type: 'NATIONAL_ID', value: '#(nid)', customerId: '#(ids.ana)' }
When method post
Then status 201
And match response.token == '#regex tok_nid_.+'
And match response.masked contains '**-****'
```

- El mismo valor del mismo cliente da **siempre el mismo token** (la segunda vez, 200 en vez de 201).
- Volver al dato en claro (`/detokenize`) exige **rol y finalidad** (`purpose`): Cumplimiento con `KYC_REVIEW`,
  el propio cliente con `CUSTOMER_REQUEST`. Un asesor no puede (403 `DETOKENIZE_NOT_ALLOWED`).
- Si otro servicio necesita el dato una vez, se le da un **pasaporte** (`data-passport`, 6503): sirve solo para un
  destinatario (`X-Caller-Service`), un número de lecturas y un tiempo. Gastado: 410 `PASSPORT_EXHAUSTED`.

## La norma

1. Comprueba **lo que NO sale**: que el valor en claro no aparece en la respuesta (`match texto !contains nid`).
2. Prueba cada finalidad prohibida, no solo la buena.
3. Un 410 *Gone* significa "existió y ya no": distinto de un 404.

## El fundamento

Privacidad por diseño no se demuestra con el caso feliz: se demuestra buscando el dato donde **no** debería estar.

## Tus ejercicios

`academia corregir L22`.
