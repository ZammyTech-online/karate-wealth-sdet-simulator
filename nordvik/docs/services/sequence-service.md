# sequence-service

Secuencias con nombre que devuelven identificadores correlativos con prefijo y relleno de ceros.

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6002 |
| Oleada | 0 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6002/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (9)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/sequences` |  |
| `POST` | `/api/v1/sequences` |  |
| `DELETE` | `/api/v1/sequences/{name}` |  |
| `GET` | `/api/v1/sequences/{name}` |  |
| `POST` | `/api/v1/sequences/{name}/next` |  |
| `POST` | `/api/v1/sequences/{name}/reset` |  |
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

### GET /api/v1/sequences



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] name`, `[] prefix`, `[] padding`, `[] current`, `[] createdAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/sequences



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `name` | string, puede ser null |
| `prefix` | string, puede ser null |
| `padding` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `name`, `prefix`, `padding`, `current`, `createdAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/sequences/{name}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `name` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/sequences/{name}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `name` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `name`, `prefix`, `padding`, `current`, `createdAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/sequences/{name}/next



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `name` | path | string | sí |
| `count` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `name`, `values`, `current` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/sequences/{name}/reset



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `name` | path | string | sí |

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
