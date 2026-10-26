# tax-agency-sim

E8 Agencia Tributaria: tablas de retención por número nacional (con API key) y ficheros anuales de control con acuse línea a línea.

| Dato | Valor |
| --- | --- |
| Dominio | X-external |
| Equipo dueño | Sistema externo (simulado) |
| Puerto | 7708 |
| Oleada | 4 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7708/swagger |
| Colección Bruno | `collections/sistema-externo-simulado` |

## Endpoints (11)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `GET` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour/reset` |  |
| `GET` | `/__admin/filings` |  |
| `POST` | `/__admin/reject-next` |  |
| `POST` | `/__admin/reset` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |
| `POST` | `/v1/filings` |  |
| `GET` | `/v1/tax-tables/{personalNumber}` |  |

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

### GET /__admin/filings



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/reject-next



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `count` | integer (int32), puede ser null |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/reset



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

### POST /v1/filings



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `reporter` | string, puede ser null |
| `year` | integer (int32), puede ser null |
| `kind` | string, puede ser null |
| `lines` | lista de objeto |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /v1/tax-tables/{personalNumber}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `personalNumber` | path | string | sí |
| `year` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |
