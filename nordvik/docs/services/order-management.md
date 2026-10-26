# order-management

Órdenes de fondos con Idempotency-Key, hora de corte, retención de efectivo, control de participaciones y ciclo de vida completo.

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6902 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | portfolio-service, product-catalog, cash-management, position-keeping, idempotency-store, permission-service, config-service, customer-registry, event-bus, clock-service |
| Publica eventos | `order.created`, `order.cancelled`, `order.sent`, `order.executed`, `order.rejected` |
| Consume eventos | `trade.executed`, `trade.rejected` |
| Swagger | http://localhost:6902/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (12)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/consumer` |  |
| `GET` | `/api/v1/internal/orders` |  |
| `GET` | `/api/v1/internal/orders/{id}` |  |
| `POST` | `/api/v1/internal/orders/mark-sent` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `POST` | `/api/v1/orders` |  |
| `GET` | `/api/v1/orders/{id}` |  |
| `POST` | `/api/v1/orders/{id}/cancel` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/orders` |  |
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

### GET /api/v1/internal/orders



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `status` | query | string | no |
| `tradeDateUpTo` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] portfolioId`, `[] customerId`, `[] isin`, `[] fundName`, `[] side`, `[] amount`, `[] units`, `[] status`, `[] tradeDate`, `[] cutOffApplied`, `[] batchId`, `[] holdId`, `[] executedUnits`, `[] nav`, `[] grossAmountSek`, `[] feeSek`, `[] netAmountSek`, `[] settlementDate`, `[] rejectionReason`, `[] initiatedBy`, `[] createdAt`, `[] updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/orders/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `portfolioId`, `customerId`, `isin`, `fundName`, `side`, `amount`, `units`, `status`, `tradeDate`, `cutOffApplied`, `batchId`, `holdId`, `executedUnits`, `nav`, `grossAmountSek`, `feeSek`, `netAmountSek`, `settlementDate`, `rejectionReason`, `initiatedBy`, `createdAt`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/internal/orders/mark-sent



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `orderIds` | lista de string |
| `batchId` | string, puede ser null |

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

### POST /api/v1/orders



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `portfolioId` | string, puede ser null |
| `isin` | string, puede ser null |
| `side` | string, puede ser null |
| `amount` | string, puede ser null |
| `units` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `portfolioId`, `customerId`, `isin`, `fundName`, `side`, `amount`, `units`, `status`, `tradeDate`, `cutOffApplied`, `batchId`, `holdId`, `executedUnits`, `nav`, `grossAmountSek`, `feeSek`, `netAmountSek`, `settlementDate`, `rejectionReason`, `initiatedBy`, `createdAt`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/orders/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `portfolioId`, `customerId`, `isin`, `fundName`, `side`, `amount`, `units`, `status`, `tradeDate`, `cutOffApplied`, `batchId`, `holdId`, `executedUnits`, `nav`, `grossAmountSek`, `feeSek`, `netAmountSek`, `settlementDate`, `rejectionReason`, `initiatedBy`, `createdAt`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/orders/{id}/cancel



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `portfolioId`, `customerId`, `isin`, `fundName`, `side`, `amount`, `units`, `status`, `tradeDate`, `cutOffApplied`, `batchId`, `holdId`, `executedUnits`, `nav`, `grossAmountSek`, `feeSek`, `netAmountSek`, `settlementDate`, `rejectionReason`, `initiatedBy`, `createdAt`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/portfolios/{portfolioId}/orders



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] portfolioId`, `[] customerId`, `[] isin`, `[] fundName`, `[] side`, `[] amount`, `[] units`, `[] status`, `[] tradeDate`, `[] cutOffApplied`, `[] batchId`, `[] holdId`, `[] executedUnits`, `[] nav`, `[] grossAmountSek`, `[] feeSek`, `[] netAmountSek`, `[] settlementDate`, `[] rejectionReason`, `[] initiatedBy`, `[] createdAt`, `[] updatedAt` |
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
