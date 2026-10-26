# mfa-service

Segundo factor: código de 6 dígitos por SMS o email (vía notification-service), 5 min, 3 intentos. Solo se guarda el hash del código.

| Dato | Valor |
| --- | --- |
| Dominio | C-identity |
| Equipo dueño | Identidad |
| Puerto | 6204 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | notification-service, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6204/swagger |
| Colección Bruno | `collections/identidad` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/mfa/challenges` |  |
| `GET` | `/api/v1/mfa/challenges/{id}` |  |
| `POST` | `/api/v1/mfa/challenges/{id}/verify` |  |
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

### POST /api/v1/mfa/challenges



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `channel` | string, puede ser null |
| `destination` | string, puede ser null |
| `purpose` | string, puede ser null |
| `language` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `userId`, `channel`, `maskedDestination`, `purpose`, `status`, `attemptsLeft`, `createdAt`, `expiresAt`, `verifiedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/mfa/challenges/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `userId`, `channel`, `maskedDestination`, `purpose`, `status`, `attemptsLeft`, `createdAt`, `expiresAt`, `verifiedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/mfa/challenges/{id}/verify



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `code` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `userId`, `channel`, `maskedDestination`, `purpose`, `status`, `attemptsLeft`, `createdAt`, `expiresAt`, `verifiedAt` |
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
