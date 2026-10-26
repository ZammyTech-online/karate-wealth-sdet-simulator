# L31 · Contratos y esquemas: que nadie rompa a nadie

## La idea

Un contrato de API (OpenAPI) o de evento (esquema) es una **promesa**. Tres herramientas para vigilarla:

**1 · Un esquema en Karate.** Defines la forma y comparas:

```gherkin
* def fondo = { isin: '#string', name: '#string', base_currency: '#string', sri: '#number', fees: '#[] #object' }
Given url catalogUrl
And path '/api/v1/funds/SE0000000001'
When method get
Then match response contains fondo
```

Ojo: `product-catalog` escribe en **snake_case** (`base_currency`); otros servicios en camelCase. Un esquema por servicio.

**2 · `schema-registry` (6111).** Los esquemas de los eventos tienen versiones y la nueva debe ser compatible
**BACKWARD** con la anterior: no se pueden añadir campos obligatorios, quitar campos ni cambiar tipos (409 `INCOMPATIBLE_SCHEMA`).

**3 · `contract-check` (7603).** Compara el OpenAPI publicado de un servicio con su respuesta real y lista las diferencias.
Con la trampa **T9** activada (`PUT chaos /api/v1/traps/T9 { enabled: true }`), product-catalog renombra `isin` a
`instrumentId` sin tocar su OpenAPI: contract-check lo detecta como `DRIFT`.

## La norma

1. Valida contra el contrato de **cada entorno**: un test que solo mira tu entorno no detecta la deriva.
2. Antes de publicar un evento con un campo nuevo, regístralo en el registro de esquemas.
3. Deja las trampas como estaban: el `Background` de esta lección resetea chaos-control.
