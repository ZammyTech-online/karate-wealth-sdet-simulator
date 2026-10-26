# sanctions-adapter

Cribado de sanciones y PEP contra SanctionScreen (E3): umbral configurable, respuestas parciales = INCOMPLETE, decisión de Cumplimiento sobre posibles coincidencias.

| Dato | Valor |
| --- | --- |
| Dominio | E-compliance |
| Equipo dueño | Cumplimiento normativo |
| Puerto | 6401 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | sanctionscreen-sim, secrets-service, customer-registry, config-service, clock-service |
| Publica eventos | `screening.completed`, `screening.decided` |
| Consume eventos | — |
| Swagger | http://localhost:6401/swagger |
| Colección Bruno | `collections/cumplimiento-normativo` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/screenings` |  |
| `POST` | `/api/v1/screenings` |  |
| `GET` | `/api/v1/screenings/{id}` |  |
| `POST` | `/api/v1/screenings/{id}/decision` |  |
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

### GET /api/v1/screenings



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | query | string | no |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/screenings



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `customerId` | string, puede ser null |
| `name` | string, puede ser null |
| `birthDate` | string, puede ser null |
| `country` | string, puede ser null |
| `lists` | lista de string |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `customerId`, `subjectRef`, `name`, `birthDate`, `status`, `threshold`, `partial`, `screenedLists`, `matches`, `ignoredBelowThreshold`, `decision`, `decisionReason`, `decidedBy`, `createdAt`, `createdBy` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/screenings/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerId`, `subjectRef`, `name`, `birthDate`, `status`, `threshold`, `partial`, `screenedLists`, `matches`, `ignoredBelowThreshold`, `decision`, `decisionReason`, `decidedBy`, `createdAt`, `createdBy` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/screenings/{id}/decision



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `decision` | string, puede ser null |
| `reason` | string, puede ser null |

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
