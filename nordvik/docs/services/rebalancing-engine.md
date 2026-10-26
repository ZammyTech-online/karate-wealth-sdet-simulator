# rebalancing-engine

Desviación frente a la asignación objetivo y saga de rebalanceo (ventas primero, compras con lo cobrado). Umbral en config (driftThresholdPp).

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6908 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | portfolio-service, valuation-service, order-management, iam-service, permission-service, config-service, event-bus, clock-service |
| Publica eventos | `rebalance.started`, `rebalance.completed`, `rebalance.failed` |
| Consume eventos | `order.executed`, `order.rejected` |
| Swagger | http://localhost:6908/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (11)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/consumer` |  |
| `DELETE` | `/api/v1/internal/consumer/failures/{eventId}` |  |
| `POST` | `/api/v1/internal/consumer/retry/{eventId}` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/drift` |  |
| `POST` | `/api/v1/portfolios/{portfolioId}/rebalance` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/rebalances` |  |
| `GET` | `/api/v1/rebalances/{id}` |  |
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

### GET /api/v1/internal/consumer



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/internal/consumer/failures/{eventId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `eventId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/internal/consumer/retry/{eventId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `eventId` | path | string | sí |

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

### GET /api/v1/portfolios/{portfolioId}/drift



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `portfolioId`, `totalValueSek`, `thresholdPp`, `maxDriftPp`, `needsRebalancing`, `lines` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/portfolios/{portfolioId}/rebalance



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `force` | boolean, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `portfolioId`, `customerId`, `status`, `initiatedBy`, `createdAt`, `finishedAt`, `failureReason`, `plannedSellSek`, `plannedBuySek`, `buyScale`, `orders` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/portfolios/{portfolioId}/rebalances



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] portfolioId`, `[] customerId`, `[] status`, `[] initiatedBy`, `[] createdAt`, `[] finishedAt`, `[] failureReason`, `[] plannedSellSek`, `[] plannedBuySek`, `[] buyScale`, `[] orders` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/rebalances/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `portfolioId`, `customerId`, `status`, `initiatedBy`, `createdAt`, `finishedAt`, `failureReason`, `plannedSellSek`, `plannedBuySek`, `buyScale`, `orders` |
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
