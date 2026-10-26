# consent-ledger

Consentimientos por propósito en un libro encadenado por hash: estado actual, historial completo y verificación de integridad. Denegado por defecto.

| Dato | Valor |
| --- | --- |
| Dominio | F-privacy |
| Equipo dueño | Privacidad y protección de datos |
| Puerto | 6502 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | customer-registry, permission-service, clock-service |
| Publica eventos | `consent.granted`, `consent.withdrawn` |
| Consume eventos | — |
| Swagger | http://localhost:6502/swagger |
| Colección Bruno | `collections/privacidad-y-proteccion-de-datos` |

## Endpoints (11)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/admin/tamper` |  |
| `POST` | `/api/v1/consents` |  |
| `GET` | `/api/v1/customers/{customerId}/consents` |  |
| `GET` | `/api/v1/customers/{customerId}/consents/history` |  |
| `GET` | `/api/v1/customers/{customerId}/consents/verify` |  |
| `GET` | `/api/v1/internal/consents/check` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/purposes` |  |
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

### POST /api/v1/admin/tamper



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `entryId` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/consents



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `customerId` | string, puede ser null |
| `purpose` | string, puede ser null |
| `granted` | boolean, puede ser null |
| `channel` | string, puede ser null |
| `text` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{customerId}/consents



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{customerId}/consents/history



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |
| `purpose` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{customerId}/consents/verify



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/consents/check



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | query | string | sí |
| `purpose` | query | string | sí |

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

### GET /api/v1/purposes



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
