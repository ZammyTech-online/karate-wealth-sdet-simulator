# L19 · Idempotencia: la misma orden dos veces no compra dos veces

## La idea

La red falla. El móvil del cliente manda "comprar 300 SEK", no recibe respuesta y **lo vuelve a mandar**.
Si Nordvik compra dos veces, el cliente pierde dinero. La solución es la cabecera **`Idempotency-Key`**:

```gherkin
* def clave = 'k-' + java.util.UUID.randomUUID()
Given url ordersUrl
And path '/api/v1/orders'
And headers erik.auth
And header Idempotency-Key = clave
And request { portfolioId: '#(cartera)', isin: 'SE0000000001', side: 'BUY', amount: '300.00' }
When method post
Then status 201
```

| Envías... | Nordvik responde |
|---|---|
| Sin clave | 400 `IDEMPOTENCY_KEY_REQUIRED` |
| Clave corta o con símbolos raros | 400 `IDEMPOTENCY_KEY_INVALID` (8-64 caracteres: letras, números, `-`, `_`) |
| Misma clave, misma petición | **la misma respuesta**, sin repetir nada, con la cabecera `Idempotent-Replayed: true` |
| Misma clave, otra petición | 422 `IDEMPOTENCY_KEY_REUSED` |

Ojo: **los errores también se guardan**. Si la primera vez la orden falló por falta de fondos, repetirla con la misma clave
devuelve el mismo 422 aunque ahora haya fondos. Para reintentar de verdad, clave nueva.

## Leer una cabecera de la respuesta

```gherkin
And match karate.response.header('idempotent-replayed') == 'true'
```

`karate.response.header()` no distingue mayúsculas de minúsculas, igual que HTTP.

## La norma

1. Una clave nueva **por operación de negocio**, no por intento.
2. Prueba siempre los cuatro casos de la tabla; el de "misma clave, otra petición" es el que más fallos encuentra.
3. Comprueba el **efecto**, no solo la respuesta: tras dos envíos, el dinero disponible ha bajado una sola vez.

## Tus ejercicios

`academia corregir L19` (cada ejercicio crea su propio cliente con dinero: tarda unos segundos).
