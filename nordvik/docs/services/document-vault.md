# document-vault

Documentos cifrados con caducidad y autodestrucción (crypto-shredding), contentHash, y enlaces de descarga de un solo uso.

| Dato | Valor |
| --- | --- |
| Dominio | H-contracts |
| Equipo dueño | Contratos y documentos |
| Puerto | 6704 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | permission-service, clock-service |
| Publica eventos | `document.stored`, `document.destroyed` |
| Consume eventos | — |
| Swagger | http://localhost:6704/swagger |
| Colección Bruno | `collections/contratos-y-documentos` |

## Endpoints (11)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/documents` |  |
| `POST` | `/api/v1/documents` |  |
| `DELETE` | `/api/v1/documents/{id}` |  |
| `GET` | `/api/v1/documents/{id}` |  |
| `POST` | `/api/v1/documents/{id}/links` |  |
| `GET` | `/api/v1/internal/documents/{id}` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v1/links/{token}` |  |
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

### GET /api/v1/documents



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `contractId` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] kind`, `[] mimeType`, `[] fileName`, `[] size`, `[] contentHash`, `[] contractId`, `[] customerRef`, `[] version`, `[] status`, `[] createdAt`, `[] expiresAt`, `[] destroyedAt`, `[] content` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/documents



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `kind` | string, puede ser null |
| `mimeType` | string, puede ser null |
| `fileName` | string, puede ser null |
| `contentBase64` | string, puede ser null |
| `content` | cualquiera, puede ser null |
| `contractId` | string, puede ser null |
| `customerId` | string, puede ser null |
| `expiresInSeconds` | integer (int32), puede ser null |
| `purpose` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `kind`, `mimeType`, `fileName`, `size`, `contentHash`, `contractId`, `customerRef`, `version`, `status`, `createdAt`, `expiresAt`, `destroyedAt`, `content` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### DELETE /api/v1/documents/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `kind`, `mimeType`, `fileName`, `size`, `contentHash`, `contractId`, `customerRef`, `version`, `status`, `createdAt`, `expiresAt`, `destroyedAt`, `content` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/documents/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `kind`, `mimeType`, `fileName`, `size`, `contentHash`, `contractId`, `customerRef`, `version`, `status`, `createdAt`, `expiresAt`, `destroyedAt`, `content` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/documents/{id}/links



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `ttlSeconds` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `token`, `url`, `expiresAt`, `ttlSeconds` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/documents/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `kind`, `mimeType`, `fileName`, `size`, `contentHash`, `contractId`, `customerRef`, `version`, `status`, `createdAt`, `expiresAt`, `destroyedAt`, `content` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/outbox



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `after` | query | integer (int64) | no |
| `limit` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/links/{token}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `token` | path | string | sí |

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
