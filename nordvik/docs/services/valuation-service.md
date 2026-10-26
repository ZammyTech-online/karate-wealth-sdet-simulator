# valuation-service

Valoración de carteras a una fecha: participaciones × NAV × tipo de cambio, con coste, plusvalía latente y peso de cada fondo.

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6906 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | position-keeping, market-data, fx-rates, product-catalog, portfolio-service, permission-service, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6906/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (5)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/internal/portfolios/{portfolioId}/valuation` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/valuation` |  |
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

### GET /api/v1/internal/portfolios/{portfolioId}/valuation



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |
| `date` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `portfolioId`, `customerId`, `valuationDate`, `priceDate`, `currency`, `totalValueSek`, `totalCostSek`, `unrealizedGainSek`, `unrealizedGainPercent`, `lines` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/portfolios/{portfolioId}/valuation



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |
| `date` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `portfolioId`, `customerId`, `valuationDate`, `priceDate`, `currency`, `totalValueSek`, `totalCostSek`, `unrealizedGainSek`, `unrealizedGainPercent`, `lines` |
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
