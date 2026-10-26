# iban-service

Genera IBAN de prueba válidos de bancos ficticios y valida IBAN (longitud por país y dígitos de control mod 97).

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6007 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6007/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/banks` |  |
| `POST` | `/api/v1/ibans` |  |
| `POST` | `/api/v1/ibans/validate` |  |
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

### GET /api/v1/banks



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `country` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] country`, `[] code`, `[] name`, `[] bic` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/ibans



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `country` | string, puede ser null |
| `bankCode` | string, puede ser null |
| `seed` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `electronic`, `print`, `country`, `checkDigits`, `bankCode`, `bankName`, `bic` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/ibans/validate



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `iban` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `input`, `valid`, `electronic`, `country`, `bankCode`, `bankName`, `bic`, `reason` |
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
