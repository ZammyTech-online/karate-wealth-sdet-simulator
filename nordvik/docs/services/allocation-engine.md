# allocation-engine

Reglas de asignación: porcentaje en renta variable según riesgo, tope por horizonte corto y sustitución ESG. Siempre suma 100.

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6901 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | product-catalog |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6901/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (4)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/allocations/propose` |  |
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

### POST /api/v1/allocations/propose



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `riskProfile` | integer (int32), puede ser null |
| `horizonYears` | integer (int32), puede ser null |
| `esg` | boolean, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `riskProfile`, `horizonYears`, `esg`, `equityPercent`, `allocation`, `rationale` |
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
