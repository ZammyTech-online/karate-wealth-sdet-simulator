# jwt-forge

Tokens de prueba firmados con claims a medida: caducados, sin scope, de otro emisor, con firma rota o payload manipulado. Y un decodificador.

| Dato | Valor |
| --- | --- |
| Dominio | A-utilities |
| Equipo dueño | Plataforma de datos de prueba |
| Puerto | 6014 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6014/swagger |
| Colección Bruno | `collections/plataforma-de-datos-de-prueba` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/tokens/decode` |  |
| `POST` | `/api/v1/tokens/forge` |  |
| `GET` | `/api/v1/tokens/recipes` |  |
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

### POST /api/v1/tokens/decode



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `token` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/tokens/forge



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `sub` | string, puede ser null |
| `role` | string, puede ser null |
| `scope` | string, puede ser null |
| `customerId` | string, puede ser null |
| `advisorId` | string, puede ser null |
| `expiresInSeconds` | integer (int32), puede ser null |
| `issuer` | string, puede ser null |
| `acr` | string, puede ser null |
| `tamper` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/tokens/recipes



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
