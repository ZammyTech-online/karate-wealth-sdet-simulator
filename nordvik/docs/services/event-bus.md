# event-bus

Bus de eventos: temas, lectura por cursor, suscripciones por webhook con reintentos y cola de mensajes muertos. Sin RabbitMQ hasta el nivel 12.

| Dato | Valor |
| --- | --- |
| Dominio | O-operations |
| Equipo dueño | Plataforma de eventos |
| Puerto | 7400 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | config-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7400/swagger |
| Colección Bruno | `collections/plataforma-de-eventos` |

## Endpoints (13)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/dead-letters` |  |
| `POST` | `/api/v1/dead-letters/{id}/replay` |  |
| `GET` | `/api/v1/events` |  |
| `POST` | `/api/v1/events` |  |
| `GET` | `/api/v1/events/{eventId}` |  |
| `GET` | `/api/v1/subscriptions` |  |
| `POST` | `/api/v1/subscriptions` |  |
| `DELETE` | `/api/v1/subscriptions/{id}` |  |
| `GET` | `/api/v1/subscriptions/{id}/deliveries` |  |
| `GET` | `/api/v1/topics` |  |
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

### GET /api/v1/dead-letters



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/dead-letters/{id}/replay



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | integer (int64) | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/events



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `topics` | query | string | no |
| `topic` | query | string | no |
| `key` | query | string | no |
| `correlationId` | query | string | no |
| `after` | query | integer (int64) | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `items`, `lastSeq`, `hasMore` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/events



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 202 | `accepted`, `duplicates`, `lastSeq` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/events/{eventId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `eventId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `eventId`, `seq`, `topic`, `key`, `producer`, `occurredAt`, `correlationId`, `schemaVersion`, `payload` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/subscriptions



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/subscriptions



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `name` | string, puede ser null |
| `topic` | string, puede ser null |
| `callbackUrl` | string, puede ser null |
| `secret` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `name`, `topic`, `callbackUrl`, `active`, `cursor`, `createdAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/subscriptions/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/subscriptions/{id}/deliveries



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/topics



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] topic`, `[] events`, `[] lastEventAt` |
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
