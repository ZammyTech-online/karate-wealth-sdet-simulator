# performance-service

Rentabilidad ponderada por tiempo (TWR) y por dinero (MWR/TIR anualizada) entre dos fechas, con la serie diaria de valores y flujos.

| Dato | Valor |
| --- | --- |
| Dominio | J-investment |
| Equipo dueño | Inversiones |
| Puerto | 6907 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | position-keeping, execution-service, market-data, fx-rates, portfolio-service, permission-service, clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6907/swagger |
| Colección Bruno | `collections/inversiones` |

## Endpoints (4)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/portfolios/{portfolioId}/performance` |  |
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

### GET /api/v1/portfolios/{portfolioId}/performance



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `portfolioId` | path | string | sí |
| `from` | query | string | no |
| `to` | query | string | no |
| `series` | query | boolean | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `portfolioId`, `from`, `to`, `tradingDays`, `startValueSek`, `endValueSek`, `netFlowsSek`, `gainSek`, `twrPercent`, `mwrPercentAnnualized`, `series`, `method` |
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
