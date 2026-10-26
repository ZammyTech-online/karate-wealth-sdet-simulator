# order-router

Ventana de lotes: a la hora de corte agrupa las órdenes por fecha de negociación y las envía al custodio. Reintenta si el custodio no está.

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6903 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | order-management, custody-adapter, config-service, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6903/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/batches` |  |
| `GET` | `/api/v1/batches/{batchId}` |  |
| `POST` | `/api/v1/batches/run` |  |
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

### GET /api/v1/batches



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `tradeDate` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] batchId`, `[] tradeDate`, `[] orderIds`, `[] status`, `[] error`, `[] createdAt`, `[] trigger` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/batches/{batchId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `batchId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `batchId`, `tradeDate`, `orderIds`, `status`, `error`, `createdAt`, `trigger` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/batches/run



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `batches`, `orders`, `sent`, `errors` |
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
