# nordpay-sim

E6 NordPay: transferencias firmadas con JWS, estados ACCP/ACSC/RJCT por webhook, verificación de titularidad, abonos entrantes y extracto camt.053.

| Dato | Valor |
| --- | --- |
| Dominio | X-external |
| Equipo dueño | Sistema externo (simulado) |
| Puerto | 7706 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7706/swagger |
| Colección Bruno | `collections/sistema-externo-simulado` |

## Endpoints (14)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `POST` | `/__admin/accounts` |  |
| `GET` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour/reset` |  |
| `POST` | `/__admin/incoming-credits` |  |
| `POST` | `/__admin/process` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |
| `POST` | `/nordpay/v1/account-verifications` |  |
| `GET` | `/nordpay/v1/accounts/{iban}/statement` |  |
| `POST` | `/nordpay/v1/notification-url` |  |
| `POST` | `/nordpay/v1/payments` |  |
| `GET` | `/nordpay/v1/payments/{paymentId}` |  |

### POST /__admin/accounts



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `iban` | string, puede ser null |
| `nationalId` | string, puede ser null |
| `name` | string, puede ser null |
| `status` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /__admin/behaviour



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/behaviour



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `mode` | string, puede ser null |
| `delayMs` | integer (int32), puede ser null |
| `failNext` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/behaviour/reset



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/incoming-credits



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `debtorIban` | string, puede ser null |
| `debtorName` | string, puede ser null |
| `amount` | string, puede ser null |
| `reference` | string, puede ser null |
| `bookingDate` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/process



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

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

### POST /nordpay/v1/account-verifications



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

### GET /nordpay/v1/accounts/{iban}/statement



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `iban` | path | string | sí |
| `date` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /nordpay/v1/notification-url



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `url` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /nordpay/v1/payments



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /nordpay/v1/payments/{paymentId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `paymentId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |
