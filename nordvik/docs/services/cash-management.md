# cash-management

Saldo contable (ledger), retenciones y saldo disponible del efectivo de cada cliente. Las retenciones son idempotentes por referencia.

| Dato | Valor |
| --- | --- |
| Dominio | K-payments |
| Equipo dueño | Pagos y contabilidad |
| Puerto | 7004 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | ledger-service, customer-registry, permission-service, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7004/swagger |
| Colección Bruno | `collections/pagos-y-contabilidad` |

## Endpoints (10)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/customers/{customerId}/cash` |  |
| `GET` | `/api/v1/holds` |  |
| `POST` | `/api/v1/holds` |  |
| `GET` | `/api/v1/holds/{id}` |  |
| `POST` | `/api/v1/holds/{id}/capture` |  |
| `POST` | `/api/v1/holds/{id}/release` |  |
| `GET` | `/api/v1/internal/customers/{customerId}/cash` |  |
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

### GET /api/v1/customers/{customerId}/cash



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `currency`, `booked`, `reserved`, `available`, `activeHolds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/holds



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | query | string | no |
| `reference` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] customerId`, `[] amount`, `[] reference`, `[] reason`, `[] status`, `[] createdAt`, `[] closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/holds



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `customerId` | string, puede ser null |
| `amount` | string, puede ser null |
| `reference` | string, puede ser null |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `customerId`, `amount`, `reference`, `reason`, `status`, `createdAt`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/holds/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerId`, `amount`, `reference`, `reason`, `status`, `createdAt`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/holds/{id}/capture



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerId`, `amount`, `reference`, `reason`, `status`, `createdAt`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/holds/{id}/release



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerId`, `amount`, `reference`, `reason`, `status`, `createdAt`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/customers/{customerId}/cash



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `currency`, `booked`, `reserved`, `available`, `activeHolds` |
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
