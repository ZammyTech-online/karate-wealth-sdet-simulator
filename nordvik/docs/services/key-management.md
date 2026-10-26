# key-management

Claves AES-256 por sujeto con versiones, rotación, cifrado/descifrado y crypto-shredding (triturar la clave hace ilegible el dato en todas partes).

| Dato | Valor |
| --- | --- |
| Dominio | F-privacy |
| Equipo dueño | Privacidad y protección de datos |
| Puerto | 6501 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | `key.created`, `key.rotated`, `key.shredded` |
| Consume eventos | — |
| Swagger | http://localhost:6501/swagger |
| Colección Bruno | `collections/privacidad-y-proteccion-de-datos` |

## Endpoints (14)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/decrypt` |  |
| `GET` | `/api/v1/internal/keys/{id}/versions/{version}` |  |
| `POST` | `/api/v1/internal/keys/resolve` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `POST` | `/api/v1/internal/subjects/shred` |  |
| `GET` | `/api/v1/keys` |  |
| `POST` | `/api/v1/keys` |  |
| `GET` | `/api/v1/keys/{id}` |  |
| `POST` | `/api/v1/keys/{id}/encrypt` |  |
| `POST` | `/api/v1/keys/{id}/rotate` |  |
| `POST` | `/api/v1/keys/{id}/shred` |  |
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

### POST /api/v1/decrypt



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `plaintext` | string, puede ser null |
| `ciphertext` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/keys/{id}/versions/{version}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |
| `version` | path | integer (int32) | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/internal/keys/resolve



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `subject` | string, puede ser null |
| `purpose` | string, puede ser null |

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

### POST /api/v1/internal/subjects/shred



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `subject` | string, puede ser null |
| `purpose` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/keys



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `subject` | query | string | no |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/keys



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `subject` | string, puede ser null |
| `purpose` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `subject`, `purpose`, `currentVersion`, `status`, `createdAt`, `rotatedAt`, `shreddedAt`, `shredReason`, `versions` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/keys/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `subject`, `purpose`, `currentVersion`, `status`, `createdAt`, `rotatedAt`, `shreddedAt`, `shredReason`, `versions` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/keys/{id}/encrypt



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `plaintext` | string, puede ser null |
| `ciphertext` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/keys/{id}/rotate



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `subject`, `purpose`, `currentVersion`, `status`, `createdAt`, `rotatedAt`, `shreddedAt`, `shredReason`, `versions` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/keys/{id}/shred



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `subject`, `purpose`, `currentVersion`, `status`, `createdAt`, `rotatedAt`, `shreddedAt`, `shredReason`, `versions` |
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
