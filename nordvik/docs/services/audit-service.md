# audit-service

Registro de auditoría inmutable (cadena SHA-256). Recibe registros directos y copia cada evento del bus. No admite PUT ni DELETE.

| Dato | Valor |
| --- | --- |
| Dominio | O-operations |
| Equipo dueño | Cumplimiento técnico |
| Puerto | 7402 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | event-bus, clock-service |
| Publica eventos | — |
| Consume eventos | `*` |
| Swagger | http://localhost:7402/swagger |
| Colección Bruno | `collections/cumplimiento-tecnico` |

## Endpoints (15)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `POST` | `/_test/audit/{seq}/tamper` |  |
| `POST` | `/_test/audit/repair` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/audit` |  |
| `POST` | `/api/v1/audit` |  |
| `DELETE` | `/api/v1/audit/{id}` |  |
| `GET` | `/api/v1/audit/{id}` |  |
| `PATCH` | `/api/v1/audit/{id}` |  |
| `PUT` | `/api/v1/audit/{id}` |  |
| `GET` | `/api/v1/audit/verify` |  |
| `GET` | `/api/v1/internal/consumer` |  |
| `DELETE` | `/api/v1/internal/consumer/failures/{eventId}` |  |
| `POST` | `/api/v1/internal/consumer/retry/{eventId}` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |

### POST /_test/audit/{seq}/tamper



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `seq` | path | integer (int64) | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /_test/audit/repair



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

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

### GET /api/v1/audit



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `actor` | query | string | no |
| `action` | query | string | no |
| `resource` | query | string | no |
| `correlationId` | query | string | no |
| `source` | query | string | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `items`, `total` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/audit



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `actor` | string, puede ser null |
| `action` | string, puede ser null |
| `resource` | string, puede ser null |
| `outcome` | string, puede ser null |
| `details` | cualquiera, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `seq`, `id`, `at`, `actor`, `action`, `resource`, `outcome`, `correlationId`, `source`, `details`, `prevHash`, `hash` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/audit/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/audit/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `seq`, `id`, `at`, `actor`, `action`, `resource`, `outcome`, `correlationId`, `source`, `details`, `prevHash`, `hash` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PATCH /api/v1/audit/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PUT /api/v1/audit/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/audit/verify



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `valid`, `records`, `brokenAtSeq`, `reason`, `lastHash` |
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
