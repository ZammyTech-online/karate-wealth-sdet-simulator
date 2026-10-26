# calendar-service

Calendario bursátil de XSTO, XOSL, XCSE y XHEL: festivos, días hábiles, T+N y hora de corte de las 15:00.

| Dato | Valor |
| --- | --- |
| Dominio | I-market |
| Equipo dueño | Plataforma |
| Puerto | 6804 |
| Oleada | 0 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | clock-service |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6804/swagger |
| Colección Bruno | `collections/plataforma` |

## Endpoints (9)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `GET` | `/api/v1/calendar/{market}/holidays` |  |
| `GET` | `/api/v1/calendar/{market}/is-trading-day` |  |
| `GET` | `/api/v1/calendar/{market}/next-trading-day` |  |
| `GET` | `/api/v1/calendar/{market}/today` |  |
| `GET` | `/api/v1/calendar/{market}/trading-days` |  |
| `GET` | `/api/v1/markets` |  |
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

### GET /api/v1/calendar/{market}/holidays



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `market` | path | string | sí |
| `year` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `market`, `year`, `count`, `holidays` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/calendar/{market}/is-trading-day



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `market` | path | string | sí |
| `date` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `market`, `date`, `weekday`, `tradingDay`, `reason`, `holidayName` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/calendar/{market}/next-trading-day



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `market` | path | string | sí |
| `date` | query | string | no |
| `offset` | query | integer (int32) | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `market`, `from`, `offset`, `date` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/calendar/{market}/today



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `market` | path | string | sí |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `market`, `today`, `localTime`, `tradingDay`, `cutOff`, `beforeCutOff`, `nextTradingDay` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/calendar/{market}/trading-days



**Parámetros**

| Nombre | Dónde | Tipo | Obligatorio |
| --- | --- | --- | --- |
| `market` | path | string | sí |
| `from` | query | string | no |
| `to` | query | string | no |

**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `market`, `from`, `to`, `count`, `days` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/markets



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `[] code`, `[] name`, `[] country`, `[] currency` |
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
