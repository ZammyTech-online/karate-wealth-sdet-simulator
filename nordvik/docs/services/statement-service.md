# statement-service

Extractos mensuales y trimestrales de una cartera: valor inicial y final, órdenes ejecutadas y variación, en JSON y PDF.

| Dato | Valor |
| --- | --- |
| Dominio | N-tax |
| Equipo dueño | Fiscalidad |
| Puerto | 7303 |
| Oleada | 4 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | portfolio-service, valuation-service, order-management, permission-service, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:7303/swagger |
| Colección Bruno | `collections/fiscalidad` |

## Endpoints (5)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/statements/{period}` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/statements/{period}/pdf` |  |
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

### GET /api/v1/portfolios/{portfolioId}/statements/{period}



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |
| `period` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | Success |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/portfolios/{portfolioId}/statements/{period}/pdf



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |
| `period` | path | string | sí |

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
