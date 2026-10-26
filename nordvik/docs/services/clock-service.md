# clock-service

Reloj virtual del sistema. Todos los servicios preguntan aquí la hora; tus tests pueden viajar en el tiempo.

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6003 |
| Oleada | 0 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Swagger | http://localhost:6003/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/clock/advance` |  |
| `POST` | `/api/v1/clock/freeze` |  |
| `GET` | `/api/v1/clock/now` |  |
| `POST` | `/api/v1/clock/reset` |  |
| `POST` | `/api/v1/clock/travel` |  |
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

### POST /api/v1/clock/advance



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `seconds` | integer (int64), puede ser null |
| `minutes` | integer (int64), puede ser null |
| `hours` | integer (int64), puede ser null |
| `days` | integer (int64), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `now`, `utc`, `epochMillis`, `timezone`, `date`, `time`, `mode`, `offsetSeconds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/clock/freeze



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `at` | string (date-time), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `now`, `utc`, `epochMillis`, `timezone`, `date`, `time`, `mode`, `offsetSeconds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/clock/now



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `now`, `utc`, `epochMillis`, `timezone`, `date`, `time`, `mode`, `offsetSeconds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/clock/reset



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `now`, `utc`, `epochMillis`, `timezone`, `date`, `time`, `mode`, `offsetSeconds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/clock/travel



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `to` | string (date-time), puede ser null |
| `offsetSeconds` | integer (int64), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `now`, `utc`, `epochMillis`, `timezone`, `date`, `time`, `mode`, `offsetSeconds` |
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
