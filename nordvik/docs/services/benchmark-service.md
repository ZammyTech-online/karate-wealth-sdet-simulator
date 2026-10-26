# benchmark-service

Índices de referencia NVX con un valor por día hábil, rentabilidad entre fechas y comparación fondo frente a índice.

| Dato | Valor |
| --- | --- |
| Dominio | I-market |
| Equipo dueño | Inversiones |
| Puerto | 6807 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | market-data |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6807/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (7)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/benchmarks` |  |
| `GET` | `/api/v1/benchmarks/{code}/levels` |  |
| `GET` | `/api/v1/benchmarks/{code}/return` |  |
| `GET` | `/api/v1/compare` |  |
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

### GET /api/v1/benchmarks



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] code`, `[] name`, `[] currency`, `[] description` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/benchmarks/{code}/levels



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `code` | path | string | sí |
| `from` | query | string | no |
| `to` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `code`, `from`, `to`, `count`, `levels` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/benchmarks/{code}/return



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `code` | path | string | sí |
| `from` | query | string | no |
| `to` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `code`, `from`, `to`, `startLevel`, `endLevel`, `returnPercent` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/compare



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | query | string | no |
| `benchmark` | query | string | no |
| `from` | query | string | no |
| `to` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `benchmark`, `from`, `to`, `fundReturnPercent`, `benchmarkReturnPercent`, `differencePercent`, `beatBenchmark` |
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
