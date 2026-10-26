# contract-template-service

Plantillas versionadas: contrato marco (v1-v3), mandato de asesoramiento (v1-v2), gestión discrecional y consentimiento de grabación.

| Dato | Valor |
| --- | --- |
| Dominio | H-contracts |
| Equipo dueño | Contratos y documentos |
| Puerto | 6701 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6701/swagger |
| Colección Bruno | `collections/contratos-y-documentos` |

## Endpoints (7)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/contract-templates` |  |
| `GET` | `/api/v1/contract-templates/{type}/current` |  |
| `GET` | `/api/v1/contract-templates/{type}/versions` |  |
| `GET` | `/api/v1/contract-templates/{type}/versions/{version}` |  |
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

### GET /api/v1/contract-templates



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] type`, `[] currentVersion`, `[] versions`, `[] parties` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/contract-templates/{type}/current



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `type` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `type`, `version`, `status`, `effectiveFrom`, `language`, `title`, `parties`, `clauses` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/contract-templates/{type}/versions



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `type` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] type`, `[] version`, `[] status`, `[] effectiveFrom`, `[] language`, `[] title`, `[] parties`, `[] clauses` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/contract-templates/{type}/versions/{version}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `type` | path | string | sí |
| `version` | path | integer (int32) | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `type`, `version`, `status`, `effectiveFrom`, `language`, `title`, `parties`, `clauses` |
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
