# L23 · Caos y resiliencia: provocar fallos a propósito

## La idea

Un sistema real falla: latencias, 503, simuladores caídos. Nordvik te deja **provocar** esos fallos para probar que
tu cliente (y tus tests) reaccionan bien.

**1 · Para una sola petición**, la cabecera `X-Chaos-Scenario`:

```gherkin
Given url uuidUrl
And path '/health'
And header X-Chaos-Scenario = 'status=503'
When method get
Then status 503
And match response.code == 'CHAOS_INJECTED'
```

Valores: `status=500` (cualquier 4xx/5xx), `latency=300` (milisegundos), o los dos: `latency=200;status=503`.

**2 · Para un servicio**, una regla en `chaos-control` (7600) que dura N veces:

```gherkin
Given url chaosUrl
And path '/api/v1/faults'
And request { service: 'uuid-service', pathPrefix: '/api/v1/uuids', status: 503, times: 2 }
When method post
Then status 201
```

**3 · Un simulador entero**, desde `mock-control` (7601): `PUT /api/v1/mocks/{nombre}/behaviour { mode: 'down' }`.

Karate te da `responseTime` (ms) para medir latencias y `retry until` para reintentar hasta que algo se cumpla.

## La norma

1. **Deja todo como estaba**: `POST /api/v1/reset` en chaos-control y `POST /api/v1/mocks/reset` en mock-control.
   En esta lección el `Background` lo hace por ti.
2. Un fallo inyectado lleva la cabecera `X-Chaos-Injected`: así distingues tu caos de un fallo real.
3. Reintentar está bien para fallos **transitorios** (503); nunca reintentes un 400.

## El fundamento

La resiliencia no se supone: se prueba rompiendo cosas en un entorno donde romper es gratis.
