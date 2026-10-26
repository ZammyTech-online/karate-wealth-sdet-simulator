# L18 · Consistencia eventual: esperar sin dormir

## La idea

En Nordvik muchas cosas **no pasan en la misma petición**. Cuando un cliente transfiere dinero:

```
cliente ──transferencia──▶ NordPay (banco simulado, 7706)
                               │ webhook camt.054
                               ▼
                        nordpay-adapter (7502) ──evento──▶ event-bus ──▶ payments-gateway (7000)
                                                                          │ ingreso RECEIVED
                                                                          ▼
                                                                   ledger · cash-management
```

Tu petición al banco responde **201 en milisegundos**, pero el ingreso tarda un poco en aparecer como `RECEIVED`.
Si compruebas justo después, tu test falla *a veces*: es un **test inestable** (*flaky*).

## La herramienta: `retry until`

```gherkin
* configure retry = { count: 30, interval: 500 }
Given url paymentsUrl
And path '/api/v1/payments', depositoId
And headers olof.auth
And retry until response.status == 'RECEIVED'
When method get
Then status 200
```

Karate repite la petición hasta que la condición se cumple (o se agotan los intentos y falla con un mensaje claro).

## La norma

1. **Nunca** `Thread.sleep` ni `karate.pause` fijos: o esperas de más (suite lenta) o de menos (suite inestable).
2. `retry until` va **antes** de `method`, y la condición es JavaScript sobre `response` y `responseStatus`.
3. Pon un techo razonable: `count × interval` debe ser el doble del tiempo normal, no diez veces más.
4. Si la condición puede llegar a un estado final malo (`REJECTED`), inclúyelo: `response.status == 'RECEIVED' || response.status == 'REJECTED'`, y luego comprueba cuál fue.

## El fundamento

El tiempo de espera es parte del contrato del servicio. Si un ingreso tarda 20 s en vez de 1 s, `retry until`
lo descubre: tu techo es tu SLA.

## Tus ejercicios

Usan el ayudante `_cliente-con-dinero.feature`, que hace todo el viaje de un ingreso (léelo: es la mejor documentación del flujo).
`academia corregir L18`.
