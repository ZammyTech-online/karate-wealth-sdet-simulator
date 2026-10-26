# L06 · Los errores también se prueban: 400, 404 y RFC 9457

**En esta lección** pruebas que el sistema falla **bien**: con el código correcto y un mensaje útil.

## La idea

Todos los servicios de Nordvik devuelven los errores con el mismo formato, el estándar **RFC 9457 (Problem Details)**:

```json
{
  "type": "https://errors.nordvik.local/no-price-for-date",
  "title": "No encontrado",
  "status": 404,
  "detail": "El 2026-06-19 no es día hábil en XSTO (Víspera de San Juan).",
  "instance": "/api/v1/prices/SE0000000001",
  "code": "NO_PRICE_FOR_DATE",
  "correlationId": "corr-1cc160ccd05a4492",
  "service": "market-data",
  "hint": "No hay NAV en días no hábiles..."
}
```

`code` es lo que tu test debe comprobar; `detail` es para personas; `hint` es la pista del **modo didáctico** (desaparece en el nivel 9 o con la cabecera `X-Learning-Mode: false`).

## La norma

1. Cada caso negativo comprueba **el código HTTP y el `code`** del error.
2. No compruebes el texto exacto de `detail`: puede cambiar sin que sea un bug.
3. Un error de cliente nunca debe ser `500`.

## El fundamento

En producción, la mitad de los incidentes vienen de casos que nadie probó: fechas festivas, divisas raras, importes con coma. Un sistema financiero que acepta `100,00` como `10000` es un desastre. Los tests negativos son los que protegen el dinero.

## Así se escribe

```gherkin
Given url marketUrl
And path '/api/v1/prices', 'SE0000000001'
And param date = '2026-06-19'
When method get
Then status 404
And match response.code == 'NO_PRICE_FOR_DATE'
And match response.status == 404
```

## Tus ejercicios

`academia corregir L06`.
