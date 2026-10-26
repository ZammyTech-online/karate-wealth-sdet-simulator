# idempotency-store

Almacén de claves de idempotencia con el hash del cuerpo y la respuesta guardada. Lo usan order-management y payments-gateway.

| Dato | Valor |
| --- | --- |
| Dominio | B-platform |
| Equipo dueño | Plataforma |
| Puerto | 6103 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6103/swagger |
| Colección Bruno | `collections/plataforma` |

## Endpoints (7)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `DELETE` | `/api/v1/keys/{scope}/{key}` |  |
| `GET` | `/api/v1/keys/{scope}/{key}` |  |
| `PUT` | `/api/v1/keys/{scope}/{key}` |  |
| `POST` | `/api/v1/keys/{scope}/{key}/response` |  |
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

### DELETE /api/v1/keys/{scope}/{key}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `scope` | path | string | sí |
| `key` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/keys/{scope}/{key}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `scope` | path | string | sí |
| `key` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `scope`, `key`, `requestHash`, `state`, `status`, `body`, `location`, `createdAt`, `expiresAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PUT /api/v1/keys/{scope}/{key}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `scope` | path | string | sí |
| `key` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `requestHash` | string, puede ser null |
| `ttlSeconds` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `scope`, `key`, `requestHash`, `state`, `status`, `body`, `location`, `createdAt`, `expiresAt` |
| 201 | `scope`, `key`, `requestHash`, `state`, `status`, `body`, `location`, `createdAt`, `expiresAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/keys/{scope}/{key}/response



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `scope` | path | string | sí |
| `key` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `status` | integer (int32), puede ser null |
| `body` | cualquiera, puede ser null |
| `location` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `scope`, `key`, `requestHash`, `state`, `status`, `body`, `location`, `createdAt`, `expiresAt` |
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
