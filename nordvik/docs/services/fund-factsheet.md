# fund-factsheet

Ficha comercial del fondo por idioma: objetivo, explicación del riesgo, comisiones y rentabilidad a 1, 3 y 12 meses.

| Dato | Valor |
| --- | --- |
| Dominio | I-market |
| Equipo dueño | Inversiones |
| Puerto | 6805 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | SnakeCase |
| Depende de | product-catalog, market-data, clock-service |
| Swagger | http://localhost:6805/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (4)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/factsheets/{isin}` |  |
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

### GET /api/v1/factsheets/{isin}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |
| `lang` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `name`, `language`, `category`, `currency`, `sri`, `objective`, `risk_text`, `fees`, `performance`, `warning` |
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
