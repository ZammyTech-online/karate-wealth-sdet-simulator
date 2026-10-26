# antivirus-scan

Escaneo de adjuntos: EICAR = INFECTED, tipo real por magic bytes frente al declarado, límite de 5 MB. Acepta multipart o el fichero en el cuerpo.

| Dato | Valor |
| --- | --- |
| Dominio | H-contracts |
| Equipo dueño | Contratos y documentos |
| Puerto | 6706 |
| Oleada | 2 |
| Versión | 1.0.0 |
| Convención JSON | CamelCase |
| Depende de | — |
| Publica eventos | — |
| Consume eventos | — |
| Swagger | http://localhost:6706/swagger |
| Colección Bruno | `collections/contratos-y-documentos` |

## Endpoints (5)

| Método | Ruta | Para qué |
| --- | --- | --- |
| `DELETE` | `/_test/data` |  |
| `POST` | `/api/v1/scans` |  |
| `GET` | `/api/v1/signatures` |  |
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

### POST /api/v1/scans



**Respuestas**

| Código | Contenido |
| --- | --- |
| 200 | `scanId`, `fileName`, `size`, `sha256`, `declaredMime`, `detectedMime`, `mimeMismatch`, `verdict`, `signature`, `scannedAt` |
| 4xx | Error RFC 9457: `type`, `title`, `status`, `detail`, `instance`, `code`, `correlationId`, `service`, `hint` (modo didáctico), `errors[]` (validación) |

### GET /api/v1/signatures



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
