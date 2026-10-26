# iam-service

Login OAuth2 (password, client_credentials, refresh_token), tokens JWT de 5 minutos, introspección, revocación y bloqueo tras 5 intentos fallidos.

| Dato | Valor |
| --- | --- |
| Dominio | C-identity |
| Equipo dueño | Identidad |
| Puerto | 6200 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, session-service, config-service |
| Swagger | http://localhost:6200/swagger |
| Colección Bruno | `collections/identidad` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/oauth/introspect` |  |
| `POST` | `/api/v1/oauth/revoke` |  |
| `POST` | `/api/v1/oauth/token` |  |
| `GET` | `/api/v1/users` |  |
| `GET` | `/api/v1/users/me` |  |
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

### POST /api/v1/oauth/introspect



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `token` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `active`, `sub`, `role`, `scope`, `customerId`, `advisorId`, `sid`, `exp`, `reason` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/oauth/revoke



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `token` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/oauth/token



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `access_token`, `token_type`, `expires_in`, `refresh_token`, `scope` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/users



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/users/me



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `username`, `displayName`, `role`, `customerId`, `advisorId`, `scope` |
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
