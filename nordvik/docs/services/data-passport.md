# data-passport

Pasaportes del dato: lectura de un dato personal limitada por propósito, destinatario, número de lecturas y caducidad (reloj virtual).

| Dato | Valor |
| --- | --- |
| Dominio | F-privacy |
| Equipo dueño | Privacidad y protección de datos |
| Puerto | 6503 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | pii-vault, clock-service |
| Publica eventos | `passport.issued`, `passport.read`, `passport.revoked` |
| Consume eventos | — |
| Swagger | http://localhost:6503/swagger |
| Colección Bruno | `collections/privacidad-y-proteccion-de-datos` |

## Endpoints (9)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/passports` |  |
| `POST` | `/api/v1/passports` |  |
| `GET` | `/api/v1/passports/{id}` |  |
| `POST` | `/api/v1/passports/{id}/read` |  |
| `POST` | `/api/v1/passports/{id}/revoke` |  |
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

### GET /api/v1/passports



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `token` | query | string | no |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/passports



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `token` | string, puede ser null |
| `purpose` | string, puede ser null |
| `audience` | string, puede ser null |
| `maxReads` | integer (int32), puede ser null |
| `ttlSeconds` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `token`, `purpose`, `audience`, `maxReads`, `readsLeft`, `status`, `issuedBy`, `issuedAt`, `expiresAt`, `lastReadAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/passports/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `token`, `purpose`, `audience`, `maxReads`, `readsLeft`, `status`, `issuedBy`, `issuedAt`, `expiresAt`, `lastReadAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/passports/{id}/read



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/passports/{id}/revoke



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
