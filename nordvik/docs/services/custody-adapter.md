# custody-adapter

Adaptador del custodio E4: envía lotes, recibe webhooks firmados (HMAC), deduplica y publica ejecuciones y rechazos como eventos.

| Dato | Valor |
| --- | --- |
| Dominio | P-integrations |
| Equipo dueño | Integraciones |
| Puerto | 7500 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | custodian-sim, clock-service |
| Publica eventos | `custody.execution.received`, `custody.order.rejected` |
| Consume eventos | — |
| Swagger | http://localhost:7500/swagger |
| Colección Bruno | `collections/integraciones` |

## Endpoints (9)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/batches` |  |
| `GET` | `/api/v1/batches/{batchId}` |  |
| `GET` | `/api/v1/holdings` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `POST` | `/api/v1/webhooks/custody` |  |
| `GET` | `/api/v1/webhooks/stats` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |

### DELETE /_test/data



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `prefix` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/batches



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `batchId` | string, puede ser null |
| `tradeDate` | string, puede ser null |
| `orders` | lista de objeto |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 202 | `batchId`, `tradeDate`, `orders`, `status`, `sentAt`, `executedAt`, `executions`, `rejections` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/batches/{batchId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `batchId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `batchId`, `tradeDate`, `orders`, `status`, `sentAt`, `executedAt`, `executions`, `rejections` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/holdings



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `accountId` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/outbox



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `after` | query | integer (int64) | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/webhooks/custody



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/webhooks/stats



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /health



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /info



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |
