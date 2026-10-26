# rate-limiter

Cuotas por minuto por clave. El api-gateway le pregunta en cada petición; el límite se configura en config-service.

| Dato | Valor |
| --- | --- |
| Dominio | B-platform |
| Equipo dueño | Plataforma |
| Puerto | 6101 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, config-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6101/swagger |
| Colección Bruno | `collections/plataforma` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/check` |  |
| `DELETE` | `/api/v1/limits/{key}` |  |
| `GET` | `/api/v1/limits/{key}` |  |
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

### POST /api/v1/check



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `key` | string, puede ser null |
| `cost` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `key`, `allowed`, `limit`, `used`, `remaining`, `windowStart`, `resetAt`, `retryAfterSeconds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/limits/{key}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `key` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/limits/{key}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `key` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `key`, `allowed`, `limit`, `used`, `remaining`, `windowStart`, `resetAt`, `retryAfterSeconds` |
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
