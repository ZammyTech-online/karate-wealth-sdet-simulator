# F-12 · Sistemas heredados: mainframe de ancho fijo y pensiones SOAP

```mermaid
sequenceDiagram
  participant O as Operaciones
  participant MB as legacy-mainframe-bridge 7509
  participant PP as pension-plan-service 7100
  participant LS as legacy-pensions-soap 7510
  Note over MB: ventana de lote 02:00-04:00 (reloj virtual)
  O->>MB: POST /api/v1/batches/run
  MB->>PP: todos los planes
  MB-->>O: rc 0000 · fichero HD / PL... / TR de 80 columnas
  O->>MB: POST /api/v1/inbound/balances (text/plain, líneas BL)
  MB-->>O: rc por línea (0000, 0004, 0008, 0012, 0042, 0043, 0044)
  O->>LS: SOAP 1.1 · SOAPAction GetLegacyPlans (planes anteriores a 2015)
  LS-->>O: sobre XML · errores como SOAP Fault con HTTP 500
```

| Caso | Respuesta |
|---|---|
| Lanzar el lote fuera de la ventana | 409 con `rc: "0016"` |
| Línea de longitud distinta de 80 | rc `0008` |
| Fecha imposible / importe no numérico / plan desconocido | rc `0042` / `0043` / `0044` |
| Sin cabecera SOAPAction | 500 Fault `NO_SOAPACTION` |
| Número nacional no válido / plan que no es de esa persona | 500 Fault `INVALID_PNR` / `PLAN_NOT_FOUND` |
| XML mal formado | 500 Fault `MALFORMED_XML` |

`GET /api/v1/layouts` describe cada registro (posición y longitud) y `GET /soap/LegacyPensionService?wsdl` el contrato SOAP.

## Dónde se prueba

`LegacyAndQualityTests` (EnterpriseTests.cs).
