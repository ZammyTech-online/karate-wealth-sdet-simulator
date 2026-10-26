# retention-scheduler

Políticas de retención por clase de dato, registro de datos con vencimiento, ejecuciones de purga con el reloj virtual y retención legal (legal hold).

| Dato | Valor |
| --- | --- |
| Dominio | F-privacy |
| Equipo dueño | Privacidad y protección de datos |
| Puerto | 6506 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | key-management, clock-service |
| Publica eventos | `retention.expired` |
| Consume eventos | — |
| Swagger | http://localhost:6506/swagger |
| Colección Bruno | `collections/privacidad-y-proteccion-de-datos` |

## Endpoints (12)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/items` |  |
| `POST` | `/api/v1/items` |  |
| `GET` | `/api/v1/items/{id}` |  |
| `POST` | `/api/v1/items/{id}/legal-hold` |  |
| `GET` | `/api/v1/policies` |  |
| `PUT` | `/api/v1/policies/{dataClass}` |  |
| `POST` | `/api/v1/runs` |  |
| `GET` | `/api/v1/runs/{id}` |  |
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

### GET /api/v1/items



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `status` | query | string | no |
| `dataClass` | query | string | no |
| `ownerService` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/items



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `dataClass` | string, puede ser null |
| `ownerService` | string, puede ser null |
| `ref` | string, puede ser null |
| `subject` | string, puede ser null |
| `createdAt` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `dataClass`, `ownerService`, `ref`, `subject`, `createdAt`, `dueAt`, `status`, `legalHold`, `holdReason`, `purgedAt`, `runId` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/items/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `dataClass`, `ownerService`, `ref`, `subject`, `createdAt`, `dueAt`, `status`, `legalHold`, `holdReason`, `purgedAt`, `runId` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/items/{id}/legal-hold



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `hold` | boolean, puede ser null |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/policies



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PUT /api/v1/policies/{dataClass}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `dataClass` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `retentionSeconds` | integer (int64), puede ser null |
| `action` | string, puede ser null |
| `legal` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/runs



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/runs/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

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
