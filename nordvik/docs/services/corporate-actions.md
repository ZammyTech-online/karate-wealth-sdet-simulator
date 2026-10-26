# corporate-actions

Dividendos, desdoblamientos, fusiones y cierres de fondos: anuncio, aplicación en la fecha efectiva, efectivo al ledger y participaciones a position-keeping.

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6909 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | product-catalog, position-keeping, market-data, fx-rates, ledger-service, event-bus, clock-service |
| Publica eventos | `corporate-action.announced`, `corporate-action.applied` |
| Consume eventos | — |
| Swagger | http://localhost:6909/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/corporate-actions` |  |
| `POST` | `/api/v1/corporate-actions` |  |
| `GET` | `/api/v1/corporate-actions/{id}` |  |
| `POST` | `/api/v1/corporate-actions/{id}/apply` |  |
| `GET` | `/api/v1/internal/outbox` |  |
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

### GET /api/v1/corporate-actions



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `isin` | query | string | no |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] type`, `[] isin`, `[] effectiveDate`, `[] ratio`, `[] amountPerUnit`, `[] targetIsin`, `[] description`, `[] status`, `[] createdBy`, `[] createdAt`, `[] appliedAt`, `[] affected` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/corporate-actions



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `type` | string, puede ser null |
| `isin` | string, puede ser null |
| `effectiveDate` | string, puede ser null |
| `ratio` | string, puede ser null |
| `amountPerUnit` | string, puede ser null |
| `targetIsin` | string, puede ser null |
| `description` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `type`, `isin`, `effectiveDate`, `ratio`, `amountPerUnit`, `targetIsin`, `description`, `status`, `createdBy`, `createdAt`, `appliedAt`, `affected` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/corporate-actions/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `isin`, `effectiveDate`, `ratio`, `amountPerUnit`, `targetIsin`, `description`, `status`, `createdBy`, `createdAt`, `appliedAt`, `affected` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/corporate-actions/{id}/apply



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `isin`, `effectiveDate`, `ratio`, `amountPerUnit`, `targetIsin`, `description`, `status`, `createdBy`, `createdAt`, `appliedAt`, `affected` |
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
