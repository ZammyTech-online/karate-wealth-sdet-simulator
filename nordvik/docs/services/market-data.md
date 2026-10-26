# market-data

Precios NAV diarios con 4 decimales, historial y último precio publicado (con ETag).

| Dato | Valor |
| --- | --- |
| Dominio | I-market |
| Equipo dueño | Inversiones |
| Puerto | 6802 |
| Oleada | 0 |
| Versión | 1.0.0 |
| Convención JSON | SnakeCase |
| Depende de | clock-service |
| Swagger | http://localhost:6802/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/prices/{isin}` |  |
| `GET` | `/api/v1/prices/{isin}/history` |  |
| `GET` | `/api/v1/prices/{isin}/latest` |  |
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

### GET /api/v1/prices/{isin}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |
| `date` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `date`, `nav`, `currency`, `published_at` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/prices/{isin}/history



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |
| `from` | query | string | no |
| `to` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `currency`, `from`, `to`, `count`, `prices` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/prices/{isin}/latest



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `date`, `nav`, `currency`, `published_at` |
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
