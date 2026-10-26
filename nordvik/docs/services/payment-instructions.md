# payment-instructions

Instrucciones de pago hacia NordPay: identificadores ISO 20022, cola de reintentos si el banco cae y estados SENT → SETTLED / REJECTED.

| Dato | Valor |
| --- | --- |
| Dominio | K-payments |
| Equipo dueño | Pagos y contabilidad |
| Puerto | 7001 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | nordpay-adapter, event-bus, sequence-service, clock-service |
| Publica eventos | `payment.instruction.settled`, `payment.instruction.rejected` |
| Consume eventos | `payment.bank-status.changed` |
| Swagger | http://localhost:7001/swagger |
| Colección Bruno | `collections/pagos-y-contabilidad` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/instructions` |  |
| `POST` | `/api/v1/instructions` |  |
| `GET` | `/api/v1/instructions/{id}` |  |
| `GET` | `/api/v1/internal/consumer` |  |
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

### GET /api/v1/instructions



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `paymentId` | query | string | no |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] paymentId`, `[] customerId`, `[] msgId`, `[] endToEndId`, `[] amount`, `[] maskedCreditorIban`, `[] status`, `[] reasonCode`, `[] attempts`, `[] lastError`, `[] createdAt`, `[] sentAt`, `[] finishedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/instructions



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `paymentId` | string, puede ser null |
| `customerId` | string, puede ser null |
| `amount` | string, puede ser null |
| `creditorIban` | string, puede ser null |
| `creditorName` | string, puede ser null |
| `remittance` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `paymentId`, `customerId`, `msgId`, `endToEndId`, `amount`, `maskedCreditorIban`, `status`, `reasonCode`, `attempts`, `lastError`, `createdAt`, `sentAt`, `finishedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/instructions/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `paymentId`, `customerId`, `msgId`, `endToEndId`, `amount`, `maskedCreditorIban`, `status`, `reasonCode`, `attempts`, `lastError`, `createdAt`, `sentAt`, `finishedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/consumer



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
