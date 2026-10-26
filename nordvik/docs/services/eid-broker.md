# eid-broker

Autenticación y firma con NordID (E1): orderRef, sondeo, caducidad a 3 min y borrado del resultado a los 10 min (dato efímero).

| Dato | Valor |
| --- | --- |
| Dominio | C-identity |
| Equipo dueño | Identidad |
| Puerto | 6203 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | nordid-sim, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6203/swagger |
| Colección Bruno | `collections/identidad` |

## Endpoints (7)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/eid/auth` |  |
| `GET` | `/api/v1/eid/orders/{orderRef}` |  |
| `POST` | `/api/v1/eid/orders/{orderRef}/cancel` |  |
| `POST` | `/api/v1/eid/sign` |  |
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

### POST /api/v1/eid/auth



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `personalNumber` | string, puede ser null |
| `purpose` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/eid/orders/{orderRef}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `orderRef` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `orderRef`, `type`, `status`, `hintCode`, `failureReason`, `purpose`, `maskedPersonalNumber`, `createdAt`, `expiresAt`, `completedAt`, `user`, `signature`, `signedDataHash`, `purgeAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/eid/orders/{orderRef}/cancel



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `orderRef` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /api/v1/eid/sign



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `personalNumber` | string, puede ser null |
| `visibleText` | string, puede ser null |
| `dataHash` | string, puede ser null |
| `purpose` | string, puede ser null |

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
