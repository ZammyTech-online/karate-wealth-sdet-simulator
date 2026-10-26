# preferences-service

Preferencias de comunicación del cliente. Si nunca las guardó, devuelve los valores por defecto (isDefault=true).

| Dato | Valor |
| --- | --- |
| Dominio | D-customer |
| Equipo dueño | Clientes |
| Puerto | 6303 |
| Oleada | 1 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service, customer-registry, permission-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6303/swagger |
| Colección Bruno | `collections/clientes` |

## Endpoints (6)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/customers/{customerId}/preferences` |  |
| `PUT` | `/api/v1/customers/{customerId}/preferences` |  |
| `GET` | `/api/v1/internal/customers/{customerId}/preferences` |  |
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

### GET /api/v1/customers/{customerId}/preferences



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `language`, `channel`, `marketingConsent`, `quietHours`, `isDefault`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### PUT /api/v1/customers/{customerId}/preferences



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Cuerpo que envías (JSON)**

| Campo | Tipo |
| --- | --- |
| `language` | string, puede ser null |
| `channel` | string, puede ser null |
| `marketingConsent` | boolean, puede ser null |
| `quietHours` | objeto |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `language`, `channel`, `marketingConsent`, `quietHours`, `isDefault`, `updatedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/internal/customers/{customerId}/preferences



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `customerId` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `customerId`, `language`, `channel`, `marketingConsent`, `quietHours`, `isDefault`, `updatedAt` |
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
