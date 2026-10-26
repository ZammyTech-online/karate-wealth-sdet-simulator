# contact-service

Medios de contacto del cliente con verificación por código: 3 intentos, caducidad de 10 minutos y bloqueo.

| Dato | Valor |
| --- | --- |
| Dominio | D-customer |
| Equipo dueño | Clientes |
| Puerto | 6301 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, customer-registry, permission-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6301/swagger |
| Colección Bruno | `collections/clientes` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `GET` | `/_test/contacts/{id}/code` |  |
| `DELETE` | `/_test/data` |  |
| `DELETE` | `/api/v1/contacts/{id}` |  |
| `POST` | `/api/v1/contacts/{id}/verify` |  |
| `GET` | `/api/v1/customers/{customerId}/contacts` |  |
| `POST` | `/api/v1/customers/{customerId}/contacts` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |

### GET /_test/contacts/{id}/code



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

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

### DELETE /api/v1/contacts/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/contacts/{id}/verify



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
| 200 | `id`, `customerId`, `type`, `value`, `status`, `attemptsLeft`, `createdAt`, `verifiedAt`, `codeExpiresAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{customerId}/contacts



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] customerId`, `[] type`, `[] value`, `[] status`, `[] attemptsLeft`, `[] createdAt`, `[] verifiedAt`, `[] codeExpiresAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/customers/{customerId}/contacts



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `type` | string, puede ser null |
| `value` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `customerId`, `type`, `value`, `status`, `attemptsLeft`, `createdAt`, `verifiedAt`, `codeExpiresAt` |
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
