# fee-engine

Calcula la comisión de entrada (BUY), de salida (SELL) o el coste anual de gestión proyectado (HOLD) de un importe en un fondo.

| Dato | Valor |
| --- | --- |
| Dominio | I-market |
| Equipo dueño | Inversiones |
| Puerto | 6806 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | SnakeCase |
| Depende de | product-catalog |
| Swagger | http://localhost:6806/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (4)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/fees/calculate` |  |
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

### POST /api/v1/fees/calculate



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `isin` | string, puede ser null |
| `amount` | string, puede ser null |
| `action` | string, puede ser null |
| `years` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `action`, `amount`, `currency`, `fee`, `fee_percent`, `net_amount`, `projection` |
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
