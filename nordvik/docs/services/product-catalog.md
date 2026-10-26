# product-catalog

Catálogo de fondos de Nordvik: categoría, divisa, riesgo SRI (1-7), estado y comisiones.

| Dato | Valor |
| --- | --- |
| Dominio | I-market |
| Equipo dueño | Inversiones |
| Puerto | 6801 |
| Oleada | 0 |
| Versión | 1.0.0 |
| Convención JSON | SnakeCase |
| Depende de | — |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6801/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/funds` |  |
| `GET` | `/api/v1/funds/{isin}` |  |
| `GET` | `/api/v1/funds/{isin}/fees` |  |
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

### GET /api/v1/funds



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `category` | query | string | no |
| `currency` | query | string | no |
| `min_sri` | query | integer (int32) | no |
| `max_sri` | query | integer (int32) | no |
| `status` | query | string | no |
| `sort` | query | string | no |
| `order` | query | string | no |
| `page` | query | integer (int32) | no |
| `page_size` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `items`, `page`, `page_size`, `total`, `total_pages` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/funds/{isin}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `name`, `category`, `base_currency`, `sri`, `status`, `launch_date`, `manager`, `fees`, `description` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/funds/{isin}/fees



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `fees` |
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
