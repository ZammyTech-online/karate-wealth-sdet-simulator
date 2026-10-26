# public-pension-sim

E7 Autoridad de Pensiones Públicas: sobre firmado (HMAC del cuerpo canónico), edad mínima de cobro por año de nacimiento, pensión pública estimada y mantenimiento con Retry-After.

| Dato | Valor |
| --- | --- |
| Dominio | X-external |
| Equipo dueño | Sistema externo (simulado) |
| Puerto | 7707 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7707/swagger |
| Colección Bruno | `collections/sistema-externo-simulado` |

## Endpoints (9)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `GET` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour/reset` |  |
| `POST` | `/__admin/maintenance` |  |
| `POST` | `/__admin/sign` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |
| `POST` | `/v1/envelope` |  |

### GET /__admin/behaviour



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/behaviour



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `mode` | string, puede ser null |
| `delayMs` | integer (int32), puede ser null |
| `failNext` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/behaviour/reset



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/maintenance



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `on` | boolean, puede ser null |
| `retryAfterSeconds` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/sign



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |

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

### POST /v1/envelope



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `header` | objeto |
| `body` | cualquiera, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |
