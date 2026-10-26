# permission-service

Decide permisos por rol y propiedad del recurso: el cliente ve lo suyo, el asesor a sus clientes asignados, back office según su función.

| Dato | Valor |
| --- | --- |
| Dominio | C-identity |
| Equipo dueño | Identidad |
| Puerto | 6202 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6202/swagger |
| Colección Bruno | `collections/identidad` |

## Endpoints (8)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/advisors/{advisorId}/customers` |  |
| `POST` | `/api/v1/assignments` |  |
| `DELETE` | `/api/v1/assignments/{advisorId}/{customerId}` |  |
| `POST` | `/api/v1/permissions/check` |  |
| `GET` | `/api/v1/policies` |  |
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

### GET /api/v1/advisors/{advisorId}/customers



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `advisorId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] advisorId`, `[] customerId`, `[] assignedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/assignments



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `advisorId` | string, puede ser null |
| `customerId` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `advisorId`, `customerId`, `assignedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/assignments/{advisorId}/{customerId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `advisorId` | path | string | sí |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/permissions/check



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `role` | string, puede ser null |
| `customerId` | string, puede ser null |
| `advisorId` | string, puede ser null |
| `action` | string, puede ser null |
| `resourceCustomerId` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `allowed`, `reason`, `rule` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/policies



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] role`, `[] action`, `[] scope` |
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
