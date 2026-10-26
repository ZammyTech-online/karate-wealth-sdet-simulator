# ledger-service

Contabilidad de doble entrada: asientos cuadrados, inmutables, con reversión, saldos por cuenta y balance de comprobación.

| Dato | Valor |
| --- | --- |
| Dominio | K-payments |
| Equipo dueño | Pagos y contabilidad |
| Puerto | 7003 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | `ledger.entry.posted` |
| Consume eventos | — |
| Swagger | http://localhost:7003/swagger |
| Colección Bruno | `collections/pagos-y-contabilidad` |

## Endpoints (14)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/accounts/{account}/balance` |  |
| `GET` | `/api/v1/accounts/{account}/entries` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/journal-entries` |  |
| `POST` | `/api/v1/journal-entries` |  |
| `DELETE` | `/api/v1/journal-entries/{id}` |  |
| `GET` | `/api/v1/journal-entries/{id}` |  |
| `PATCH` | `/api/v1/journal-entries/{id}` |  |
| `PUT` | `/api/v1/journal-entries/{id}` |  |
| `POST` | `/api/v1/journal-entries/{id}/reverse` |  |
| `GET` | `/api/v1/trial-balance` |  |
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

### GET /api/v1/accounts/{account}/balance



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `account` | path | string | sí |
| `asOf` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `account`, `type`, `debits`, `credits`, `balance`, `asOf`, `entries` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/accounts/{account}/entries



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `account` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] reference`, `[] description`, `[] valueDate`, `[] postedAt`, `[] reversalOf`, `[] reversedBy`, `[] lines`, `[] totalDebit`, `[] totalCredit` |
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

### GET /api/v1/journal-entries



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `reference` | query | string | no |
| `date` | query | string | no |
| `account` | query | string | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] reference`, `[] description`, `[] valueDate`, `[] postedAt`, `[] reversalOf`, `[] reversedBy`, `[] lines`, `[] totalDebit`, `[] totalCredit` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/journal-entries



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `reference` | string, puede ser null |
| `description` | string, puede ser null |
| `valueDate` | string, puede ser null |
| `lines` | lista de objeto |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `reference`, `description`, `valueDate`, `postedAt`, `reversalOf`, `reversedBy`, `lines`, `totalDebit`, `totalCredit` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/journal-entries/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/journal-entries/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `reference`, `description`, `valueDate`, `postedAt`, `reversalOf`, `reversedBy`, `lines`, `totalDebit`, `totalCredit` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PATCH /api/v1/journal-entries/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PUT /api/v1/journal-entries/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/journal-entries/{id}/reverse



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `reference`, `description`, `valueDate`, `postedAt`, `reversalOf`, `reversedBy`, `lines`, `totalDebit`, `totalCredit` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/trial-balance



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `asOf` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `asOf`, `accounts`, `totalDebits`, `totalCredits`, `balanced` |
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
