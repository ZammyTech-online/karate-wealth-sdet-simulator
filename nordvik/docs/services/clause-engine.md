# clause-engine

Evalúa las condiciones de las cláusulas con los datos del cliente y devuelve las incluidas y excluidas con su motivo.

| Dato | Valor |
| --- | --- |
| Dominio | H-contracts |
| Equipo dueño | Contratos y documentos |
| Puerto | 6702 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | contract-template-service, customer-registry, customer-address-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6702/swagger |
| Colección Bruno | `collections/contratos-y-documentos` |

## Endpoints (5)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/clauses/resolve` |  |
| `POST` | `/api/v1/conditions/evaluate` |  |
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

### POST /api/v1/clauses/resolve



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `templateType` | string, puede ser null |
| `version` | integer (int32), puede ser null |
| `customerId` | string, puede ser null |
| `facts` | object, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `templateType`, `version`, `facts`, `included`, `excluded` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/conditions/evaluate



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `condition` | string, puede ser null |
| `facts` | object, puede ser null |

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
