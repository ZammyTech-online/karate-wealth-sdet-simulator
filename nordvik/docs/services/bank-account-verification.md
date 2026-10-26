# bank-account-verification

Cuentas bancarias de destino del cliente: IBAN válido, titularidad confirmada por el banco, revisión manual si hay dudas y step-up para añadirlas.

| Dato | Valor |
| --- | --- |
| Dominio | K-payments |
| Equipo dueño | Pagos y contabilidad |
| Puerto | 7002 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | nordpay-adapter, customer-registry, permission-service, clock-service |
| Publica eventos | `bank-account.verified`, `bank-account.rejected` |
| Consume eventos | — |
| Swagger | http://localhost:7002/swagger |
| Colección Bruno | `collections/pagos-y-contabilidad` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/bank-accounts/{id}/review` |  |
| `GET` | `/api/v1/customers/{customerId}/bank-accounts` |  |
| `POST` | `/api/v1/customers/{customerId}/bank-accounts` |  |
| `GET` | `/api/v1/internal/bank-accounts/{id}` |  |
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

### POST /api/v1/bank-accounts/{id}/review



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `decision` | string, puede ser null |
| `comment` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerId`, `maskedIban`, `country`, `label`, `status`, `bankResult`, `reason`, `createdAt`, `reviewedBy` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{customerId}/bank-accounts



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] customerId`, `[] maskedIban`, `[] country`, `[] label`, `[] status`, `[] bankResult`, `[] reason`, `[] createdAt`, `[] reviewedBy` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/customers/{customerId}/bank-accounts



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `iban` | string, puede ser null |
| `label` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `customerId`, `maskedIban`, `country`, `label`, `status`, `bankResult`, `reason`, `createdAt`, `reviewedBy` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/bank-accounts/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

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
