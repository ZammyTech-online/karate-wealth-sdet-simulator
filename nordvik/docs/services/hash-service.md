# hash-service

Calcula el JSON canónico y su SHA-256 (como contentHash), SHA-256 de texto y HMAC-SHA256 de webhooks.

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6011 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6011/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (7)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/hash/canonical` |  |
| `POST` | `/api/v1/hash/sha256` |  |
| `POST` | `/api/v1/hmac` |  |
| `POST` | `/api/v1/hmac/verify` |  |
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

### POST /api/v1/hash/canonical



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `json` | cualquiera, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `canonical`, `sha256`, `bytes` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/hash/sha256



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `text` | string, puede ser null |
| `encoding` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/hmac



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `key` | string, puede ser null |
| `message` | string, puede ser null |
| `encoding` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `algorithm`, `signature`, `encoding` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/hmac/verify



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `key` | string, puede ser null |
| `message` | string, puede ser null |
| `signature` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `valid`, `expected`, `reason` |
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
