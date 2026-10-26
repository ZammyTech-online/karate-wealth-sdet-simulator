# L29 · Ficheros de ancho fijo: el mainframe

## La idea

`legacy-mainframe-bridge` (7509) traduce entre Nordvik y un mainframe que habla en **líneas de 80 columnas exactas**,
sin separadores: cada campo ocupa unas posiciones fijas (`GET /api/v1/layouts`), el texto se rellena con espacios a la
derecha y los números con ceros a la izquierda y 2 decimales implícitos (`000000012345678` = 123456.78). Los errores son
**códigos de retorno** numéricos (`rc`): `0000` bien, `0016` fuera de la ventana de lote, `0042` fecha imposible...

En Karate construyes y lees esas líneas con funciones JavaScript:

```gherkin
* def pad = function(s, n){ while (s.length < n) s = s + ' '; return s }
* def linea = pad('BL' + pad(planId, 20) + '000000012345678' + '20141231', 80)
Given url mainframeUrl
And path '/api/v1/inbound/balances'
And header Content-Type = 'text/plain'
And request linea
When method post
Then status 200
And match response.results[0].rc == '0000'
```

El lote nocturno solo se puede lanzar de **02:00 a 04:00**: usa el reloj virtual (`/api/v1/clock/freeze`) y devuélvelo
con `afterScenario`.

## La norma

1. Comprueba la **longitud exacta** de cada línea: un carácter de más desplaza todos los campos (trampa T21).
2. Compara los campos de texto con `trim()`.
3. Un `rc` distinto de `0000` no siempre es un error: `0004` es un aviso.
