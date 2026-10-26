# provider-network-sim

E10 Red de gestoras: valoraciones de pólizas que tardan 1–3 días virtuales, traspaso del dinero y firma HMAC mutua en las dos direcciones.

| Dato | Valor |
| --- | --- |
| Dominio | X-external |
| Equipo dueño | Sistema externo (simulado) |
| Puerto | 7710 |
| Oleada | 3 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7710/swagger |
| Colección Bruno | `collections/sistema-externo-simulado` |

## Endpoints (13)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `GET` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour` |  |
| `POST` | `/__admin/behaviour/reset` |  |
| `POST` | `/__admin/policies` |  |
| `GET` | `/__admin/requests` |  |
| `POST` | `/__admin/tamper` |  |
| `DELETE` | `/_test/data` |  |
| `GET` | `/health` |  |
| `GET` | `/info` |  |
| `GET` | `/v1/providers` |  |
| `POST` | `/v1/transfers` |  |
| `POST` | `/v1/valuations` |  |
| `GET` | `/v1/valuations/{id}` |  |

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

### POST /__admin/policies



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `providerCode` | string, puede ser null |
| `policyNumber` | string, puede ser null |
| `personalNumber` | string, puede ser null |
| `value` | string, puede ser null |
| `delayDays` | integer (int32), puede ser null |
| `driftPct` | number (double), puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /__admin/requests



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /__admin/tamper



**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `on` | boolean, puede ser null |

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

### GET /v1/providers



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /v1/transfers



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### POST /v1/valuations



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /v1/valuations/{id}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `id` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |
