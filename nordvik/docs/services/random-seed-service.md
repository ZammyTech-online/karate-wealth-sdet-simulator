# random-seed-service

Números y elecciones aleatorias reproducibles a partir de una semilla, para repetir un fallo con los mismos datos.

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6004 |
| Oleada | 0 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Swagger | http://localhost:6004/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/random/decimals` |  |
| `GET` | `/api/v1/random/ints` |  |
| `GET` | `/api/v1/random/pick` |  |
| `GET` | `/api/v1/seeds` |  |
| `POST` | `/api/v1/seeds` |  |
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

### GET /api/v1/random/decimals



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `seed` | query | integer (int32) | no |
| `count` | query | integer (int32) | no |
| `min` | query | string | no |
| `max` | query | string | no |
| `scale` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `seed`, `count`, `min`, `max`, `scale`, `values` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/random/ints



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `seed` | query | integer (int32) | no |
| `count` | query | integer (int32) | no |
| `min` | query | integer (int32) | no |
| `max` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `seed`, `count`, `min`, `max`, `values` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/random/pick



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `seed` | query | integer (int32) | no |
| `items` | query | string | no |
| `count` | query | integer (int32) | no |
| `unique` | query | boolean | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `seed`, `count`, `unique`, `values` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/seeds



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] value`, `[] label`, `[] createdAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/seeds



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `label` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `value`, `label`, `createdAt` |
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
