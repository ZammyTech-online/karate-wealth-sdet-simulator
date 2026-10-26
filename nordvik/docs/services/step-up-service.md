# step-up-service

Re-autenticación (acr=high) con NordID o MFA. El token resultante dura 5 minutos y lo exigen payments-gateway y bank-account-verification.

| Dato | Valor |
| --- | --- |
| Dominio | C-identity |
| Equipo dueño | Identidad |
| Puerto | 6205 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | eid-broker, mfa-service, customer-registry, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6205/swagger |
| Colección Bruno | `collections/identidad` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/step-up` |  |
| `GET` | `/api/v1/step-up/{id}` |  |
| `POST` | `/api/v1/step-up/{id}/verify` |  |
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

### POST /api/v1/step-up



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `method` | string, puede ser null |
| `reason` | string, puede ser null |
| `channel` | string, puede ser null |
| `destination` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 201 | `id`, `userId`, `method`, `status`, `reason`, `orderRef`, `challengeId`, `createdAt`, `completedAt`, `accessToken`, `expiresIn`, `acr`, `failureReason` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/step-up/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `userId`, `method`, `status`, `reason`, `orderRef`, `challengeId`, `createdAt`, `completedAt`, `accessToken`, `expiresIn`, `acr`, `failureReason` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/step-up/{id}/verify



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `code` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `id`, `userId`, `method`, `status`, `reason`, `orderRef`, `challengeId`, `createdAt`, `completedAt`, `accessToken`, `expiresIn`, `acr`, `failureReason` |
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
