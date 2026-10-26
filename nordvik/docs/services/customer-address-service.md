# customer-address-service

Domicilio actual e historial de domicilios del cliente; el código postal debe corresponder a la ciudad.

| Dato | Valor |
| --- | --- |
| Dominio | D-customer |
| Equipo dueño | Clientes |
| Puerto | 6302 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, customer-registry, permission-service, address-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6302/swagger |
| Colección Bruno | `collections/clientes` |

## Endpoints (7)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/customers/{customerId}/address` |  |
| `PUT` | `/api/v1/customers/{customerId}/address` |  |
| `GET` | `/api/v1/customers/{customerId}/address/history` |  |
| `GET` | `/api/v1/internal/customers/{customerId}/address` |  |
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

### GET /api/v1/customers/{customerId}/address



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `version`, `street`, `number`, `postalCode`, `city`, `country`, `validFrom`, `validTo` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PUT /api/v1/customers/{customerId}/address



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `street` | string, puede ser null |
| `number` | string, puede ser null |
| `postalCode` | string, puede ser null |
| `city` | string, puede ser null |
| `country` | string, puede ser null |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `version`, `street`, `number`, `postalCode`, `city`, `country`, `validFrom`, `validTo` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/customers/{customerId}/address/history



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] customerId`, `[] version`, `[] street`, `[] number`, `[] postalCode`, `[] city`, `[] country`, `[] validFrom`, `[] validTo` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/customers/{customerId}/address



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `version`, `street`, `number`, `postalCode`, `city`, `country`, `validFrom`, `validTo` |
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
