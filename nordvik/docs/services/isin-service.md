# isin-service

Genera ISIN válidos y valida el dígito de control de cualquier ISIN.

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6008 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Swagger | http://localhost:6008/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (5)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/isins` |  |
| `POST` | `/api/v1/isins/validate` |  |
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

### POST /api/v1/isins



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `country` | string, puede ser null |
| `seed` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `isin`, `country`, `nsin`, `checkDigit` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/isins/validate



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `isin` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `isin`, `formatOk`, `valid`, `expectedCheckDigit`, `reason` |
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
