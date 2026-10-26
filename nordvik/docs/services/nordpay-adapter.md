# nordpay-adapter

Adaptador del banco E6: transferencias ISO 20022 firmadas (JWS), estados por webhook, avisos de abono, titularidad de cuentas y extracto.

| Dato | Valor |
| --- | --- |
| Dominio | P-integrations |
| Equipo dueño | Integraciones |
| Puerto | 7502 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | nordpay-sim, clock-service |
| Publica eventos | `payment.bank-status.changed`, `payment.credit.received` |
| Consume eventos | — |
| Swagger | http://localhost:7502/swagger |
| Colección Bruno | `collections/integraciones` |

## Endpoints (10)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/account-verifications` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `POST` | `/api/v1/payments` |  |
| `GET` | `/api/v1/payments/{endToEndId}` |  |
| `GET` | `/api/v1/statements` |  |
| `POST` | `/api/v1/webhooks/nordpay` |  |
| `GET` | `/api/v1/webhooks/stats` |  |
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

### POST /api/v1/account-verifications



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `iban` | string, puede ser null |
| `nationalId` | string, puede ser null |
| `name` | string, puede ser null |

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

### POST /api/v1/payments



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `msgId` | string, puede ser null |
| `endToEndId` | string, puede ser null |
| `amount` | string, puede ser null |
| `creditorIban` | string, puede ser null |
| `creditorName` | string, puede ser null |
| `remittance` | string, puede ser null |
| `requestedDate` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 202 | `msgId`, `endToEndId`, `bankPaymentId`, `status`, `reasonCode`, `amount`, `creditorIban`, `sentAt`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/payments/{endToEndId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `endToEndId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `msgId`, `endToEndId`, `bankPaymentId`, `status`, `reasonCode`, `amount`, `creditorIban`, `sentAt`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/statements



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `date` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/webhooks/nordpay



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/webhooks/stats



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
