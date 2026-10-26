# notification-service

Notificaciones por email, SMS y push con preferencias, consentimiento, horas de silencio y buzón de pruebas efímero (24 h).

| Dato | Valor |
| --- | --- |
| Dominio | G-advice |
| Equipo dueño | Comunicación con clientes |
| Puerto | 6600 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | message-template-service, preferences-service, customer-registry, clock-service, event-bus |
| Publica eventos | `notification.sent` |
| Consume eventos | `order.executed`, `payment.settled`, `payment.rejected`, `deposit.received`, `contract.sent` |
| Swagger | http://localhost:6600/swagger |
| Colección Bruno | `collections/comunicacion-con-clientes` |

## Endpoints (10)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/customers/{customerId}/notifications` |  |
| `GET` | `/api/v1/internal/consumer` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/mailbox` |  |
| `POST` | `/api/v1/notifications` |  |
| `GET` | `/api/v1/notifications/{id}` |  |
| `POST` | `/api/v1/notifications/dispatch` |  |
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

### GET /api/v1/customers/{customerId}/notifications



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] customerId`, `[] channel`, `[] to`, `[] templateKey`, `[] language`, `[] templateVersion`, `[] status`, `[] reason`, `[] priority`, `[] category`, `[] reference`, `[] createdAt`, `[] scheduledFor`, `[] sentAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/consumer



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

### GET /api/v1/mailbox



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `to` | query | string | no |
| `channel` | query | string | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/notifications



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `customerId` | string, puede ser null |
| `to` | string, puede ser null |
| `channel` | string, puede ser null |
| `templateKey` | string, puede ser null |
| `language` | string, puede ser null |
| `vars` | object, puede ser null |
| `priority` | string, puede ser null |
| `category` | string, puede ser null |
| `reference` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `customerId`, `channel`, `to`, `templateKey`, `language`, `templateVersion`, `status`, `reason`, `priority`, `category`, `reference`, `createdAt`, `scheduledFor`, `sentAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/notifications/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerId`, `channel`, `to`, `templateKey`, `language`, `templateVersion`, `status`, `reason`, `priority`, `category`, `reference`, `createdAt`, `scheduledFor`, `sentAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/notifications/dispatch



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
