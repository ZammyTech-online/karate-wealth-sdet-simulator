# custodian-sim

E4 Custodio: lotes de órdenes, ejecución al NAV del día (con comisiones del folleto y cambio de divisa), webhooks firmados y extracto de posiciones.

| Dato | Valor |
| --- | --- |
| Dominio | X-external |
| Equipo dueño | Sistema externo (simulado) |
| Puerto | 7704 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, fx-rates |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7704/swagger |
| Colección Bruno | `collections/sistema-externo-simulado` |

## Endpoints (12)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `GET` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour/reset` |  |
| `POST` | `/__admin/break-holding` |  |
| `POST` | `/__admin/process` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/custody/v2/accounts/{accountId}/holdings` |  |
| `POST` | `/custody/v2/batches` |  |
| `GET` | `/custody/v2/batches/{batchId}` |  |
| `GET` | `/custody/v2/holdings` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |

### GET /__admin/behaviour



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/behaviour



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `mode` | string, puede ser null |
| `delayMs` | integer (int32), puede ser null |
| `failNext` | integer (int32), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/behaviour/reset



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/break-holding



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `accountId` | string, puede ser null |
| `isin` | string, puede ser null |
| `deltaUnits` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/process



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

### GET /custody/v2/accounts/{accountId}/holdings



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `accountId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /custody/v2/batches



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `batchId` | string, puede ser null |
| `tradeDate` | string, puede ser null |
| `callbackUrl` | string, puede ser null |
| `orders` | lista de objeto |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /custody/v2/batches/{batchId}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `batchId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /custody/v2/holdings



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
