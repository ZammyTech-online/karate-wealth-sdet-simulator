# contract-service

Contratos con plantilla versionada, cláusulas condicionales, anexos, firma múltiple con NordID, enmiendas, desistimiento, PDF de un solo uso y verificación de integridad.

| Dato | Valor |
| --- | --- |
| Dominio | H-contracts |
| Equipo dueño | Contratos y documentos |
| Puerto | 6700 |
| Oleada | 2 |
| Versión | 2.0.0 |
| Convención JSON | CamelCase |
| Depende de | contract-template-service, clause-engine, document-vault, antivirus-scan, render-service, signing-service, customer-registry, permission-service, event-bus, clock-service |
| Publica eventos | `contract.created`, `contract.sent`, `contract.signed`, `contract.activated`, `contract.invalidated`, `contract.withdrawn`, `contract.expired`, `contract.superseded`, `contract.terminated` |
| Consume eventos | — |
| Swagger | http://localhost:6700/swagger |
| Colección Bruno | `collections/contratos-y-documentos` |

## Endpoints (22)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `POST` | `/_test/contracts/{id}/bump-template` |  |
| `POST` | `/_test/contracts/{id}/tamper` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/outbox` |  |
| `GET` | `/api/v2/contracts` |  |
| `POST` | `/api/v2/contracts` |  |
| `GET` | `/api/v2/contracts/{id}` |  |
| `POST` | `/api/v2/contracts/{id}/amendments` |  |
| `POST` | `/api/v2/contracts/{id}/annexes` |  |
| `GET` | `/api/v2/contracts/{id}/render` |  |
| `POST` | `/api/v2/contracts/{id}/send` |  |
| `GET` | `/api/v2/contracts/{id}/signatures` |  |
| `POST` | `/api/v2/contracts/{id}/signing-sessions` |  |
| `GET` | `/api/v2/contracts/{id}/signing-sessions/{sessionId}` |  |
| `POST` | `/api/v2/contracts/{id}/terminate` |  |
| `POST` | `/api/v2/contracts/{id}/verify` |  |
| `POST` | `/api/v2/contracts/{id}/withdraw` |  |
| `GET` | `/api/v2/internal/contracts` |  |
| `GET` | `/api/v2/internal/contracts/{id}` |  |
| `POST` | `/api/v2/internal/contracts/{id}/signatures` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |

### POST /_test/contracts/{id}/bump-template



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /_test/contracts/{id}/tamper



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

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

### GET /api/v2/contracts



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | query | string | no |
| `customerRef` | query | string | no |
| `status` | query | string | no |
| `limit` | query | integer (int32) | no |
| `cursor` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `items`, `nextCursor`, `limit` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `type` | string, puede ser null |
| `customerId` | string, puede ser null |
| `accountType` | string, puede ser null |
| `facts` | object, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v2/contracts/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/amendments



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `accountType` | string, puede ser null |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/annexes



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v2/contracts/{id}/render



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |
| `format` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/send



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v2/contracts/{id}/signatures



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/signing-sessions



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `party` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v2/contracts/{id}/signing-sessions/{sessionId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |
| `sessionId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/terminate



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/verify



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/contracts/{id}/withdraw



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `reason` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v2/internal/contracts



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | query | string | sí |
| `type` | query | string | no |
| `status` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] id`, `[] type`, `[] templateVersion`, `[] status`, `[] customerRef`, `[] accountType`, `[] language`, `[] parties`, `[] signatures`, `[] annexes`, `[] content`, `[] contentHash`, `[] version`, `[] createdAt`, `[] createdBy`, `[] sentAt`, `[] expiresAt`, `[] activatedAt`, `[] withdrawalDeadline`, `[] amends`, `[] amendedBy`, `[] invalidReason`, `[] closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v2/internal/contracts/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `type`, `templateVersion`, `status`, `customerRef`, `accountType`, `language`, `parties`, `signatures`, `annexes`, `content`, `contentHash`, `version`, `createdAt`, `createdBy`, `sentAt`, `expiresAt`, `activatedAt`, `withdrawalDeadline`, `amends`, `amendedBy`, `invalidReason`, `closedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v2/internal/contracts/{id}/signatures



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `party` | string, puede ser null |
| `sessionId` | string, puede ser null |
| `signedHash` | string, puede ser null |
| `templateVersion` | integer (int32), puede ser null |
| `signature` | string, puede ser null |
| `signedAt` | string, puede ser null |
| `signer` | string, puede ser null |

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
