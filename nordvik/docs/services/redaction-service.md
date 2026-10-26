# redaction-service

Redacta PII de texto libre: número nacional, IBAN, email, teléfono y tarjeta, solo si validan. Modos MASK, REMOVE y TOKENIZE (vía pii-vault).

| Dato | Valor |
| --- | --- |
| Dominio | F-privacy |
| Equipo dueño | Privacidad y protección de datos |
| Puerto | 6505 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | pii-vault, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6505/swagger |
| Colección Bruno | `collections/privacidad-y-proteccion-de-datos` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/detectors` |  |
| `POST` | `/api/v1/redact` |  |
| `POST` | `/api/v1/scan` |  |
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

### GET /api/v1/detectors



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/redact



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `text` | string, puede ser null |
| `mode` | string, puede ser null |
| `customerId` | string, puede ser null |
| `types` | lista de string |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/scan



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `text` | string, puede ser null |
| `mode` | string, puede ser null |
| `customerId` | string, puede ser null |
| `types` | lista de string |

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
