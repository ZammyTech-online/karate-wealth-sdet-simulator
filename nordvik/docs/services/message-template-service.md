# message-template-service

Plantillas de email, SMS y push en sv, en y es, versionadas. Renderiza {{variables}}; si falta alguna, 422 con la lista. Idioma desconocido → en.

| Dato | Valor |
| --- | --- |
| Dominio | G-advice |
| Equipo dueño | Comunicación con clientes |
| Puerto | 6601 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6601/swagger |
| Colección Bruno | `collections/comunicacion-con-clientes` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/templates` |  |
| `GET` | `/api/v1/templates/{key}` |  |
| `POST` | `/api/v1/templates/{key}/render` |  |
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

### GET /api/v1/templates



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `channel` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] key`, `[] language`, `[] version`, `[] channel`, `[] subject`, `[] body`, `[] variables`, `[] current` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/templates/{key}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `key` | path | string | sí |
| `lang` | query | string | no |
| `version` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `key`, `language`, `version`, `channel`, `subject`, `body`, `variables`, `current` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/templates/{key}/render



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `key` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `language` | string, puede ser null |
| `version` | integer (int32), puede ser null |
| `vars` | object, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `key`, `language`, `version`, `fallbackLanguage`, `subject`, `body` |
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
