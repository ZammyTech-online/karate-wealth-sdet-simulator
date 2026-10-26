# customer-registry

Alta, consulta, modificación (con If-Match) y cierre de clientes. Enmascara el número nacional según el rol y publica un registro de cambios.

| Dato | Valor |
| --- | --- |
| Dominio | D-customer |
| Equipo dueño | Clientes |
| Puerto | 6300 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, sequence-service, permission-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6300/swagger |
| Colección Bruno | `collections/clientes` |

## Endpoints (11)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/customers` |  |
| `POST` | `/api/v1/customers` |  |
| `DELETE` | `/api/v1/customers/{id}` |  |
| `GET` | `/api/v1/customers/{id}` |  |
| `PATCH` | `/api/v1/customers/{id}` |  |
| `GET` | `/api/v1/customers/by-number/{customerNumber}` |  |
| `GET` | `/api/v1/internal/customers/{id}` |  |
| `GET` | `/api/v1/internal/customers/changes` |  |
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

### GET /api/v1/customers



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `q` | query | string | no |
| `status` | query | string | no |
| `page` | query | integer (int32) | no |
| `pageSize` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `items`, `page`, `pageSize`, `total` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/customers



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `firstName` | string, puede ser null |
| `lastName` | string, puede ser null |
| `nationalId` | string, puede ser null |
| `email` | string, puede ser null |
| `phone` | string, puede ser null |
| `language` | string, puede ser null |
| `externalRef` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `customerNumber`, `firstName`, `lastName`, `nationalId`, `birthDate`, `age`, `email`, `phone`, `language`, `status`, `version`, `createdAt`, `updatedAt`, `externalRef` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/customers/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerNumber`, `firstName`, `lastName`, `nationalId`, `birthDate`, `age`, `email`, `phone`, `language`, `status`, `version`, `createdAt`, `updatedAt`, `externalRef` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerNumber`, `firstName`, `lastName`, `nationalId`, `birthDate`, `age`, `email`, `phone`, `language`, `status`, `version`, `createdAt`, `updatedAt`, `externalRef` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PATCH /api/v1/customers/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `firstName` | string, puede ser null |
| `lastName` | string, puede ser null |
| `email` | string, puede ser null |
| `phone` | string, puede ser null |
| `language` | string, puede ser null |
| `nationalId` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerNumber`, `firstName`, `lastName`, `nationalId`, `birthDate`, `age`, `email`, `phone`, `language`, `status`, `version`, `createdAt`, `updatedAt`, `externalRef` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/by-number/{customerNumber}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerNumber` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerNumber`, `firstName`, `lastName`, `nationalId`, `birthDate`, `age`, `email`, `phone`, `language`, `status`, `version`, `createdAt`, `updatedAt`, `externalRef` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/customers/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `customerNumber`, `firstName`, `lastName`, `nationalId`, `birthDate`, `age`, `email`, `phone`, `language`, `status`, `version`, `createdAt`, `updatedAt`, `externalRef` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/customers/changes



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `since` | query | integer (int64) | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `items`, `lastSeq` |
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
