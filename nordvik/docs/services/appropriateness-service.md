# appropriateness-service

Test de conveniencia para productos complejos (SRI 6-7) sin asesoramiento: NOT_REQUIRED, APPROPRIATE o NOT_APPROPRIATE con advertencia y reconocimiento.

| Dato | Valor |
| --- | --- |
| Dominio | E-compliance |
| Equipo dueño | Cumplimiento normativo |
| Puerto | 6406 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | customer-registry, permission-service, clock-service |
| Publica eventos | `appropriateness.assessed`, `appropriateness.warning-acknowledged` |
| Consume eventos | — |
| Swagger | http://localhost:6406/swagger |
| Colección Bruno | `collections/cumplimiento-normativo` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/appropriateness-tests/{id}/acknowledge` |  |
| `POST` | `/api/v1/customers/{customerId}/appropriateness-tests` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/products/{isin}/complexity` |  |
| `GET` | `/api/v1/quiz` |  |
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

### POST /api/v1/appropriateness-tests/{id}/acknowledge



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/customers/{customerId}/appropriateness-tests



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `isin` | string, puede ser null |
| `answers` | object, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/outbox



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `after` | query | integer (int64) | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/products/{isin}/complexity



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/quiz



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
