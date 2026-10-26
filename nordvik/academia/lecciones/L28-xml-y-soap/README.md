# L28 · XML y SOAP: hablar con el sistema de pensiones antiguo

**Tramo 4.** Ya tienes la empresa completa. Arranca el perfil grande:

```
nordvik up -Profile completo
```

## La idea

Los planes de pensión anteriores a 2015 viven en un sistema antiguo, `legacy-pensions-soap` (7510), que solo habla
**SOAP 1.1**: un sobre XML, la cabecera `SOAPAction` y los errores como **SOAP Fault con HTTP 500**.
Karate lee y escribe XML de forma nativa:

```gherkin
Given url soapUrl
And path '/soap/LegacyPensionService'
And request
  """
  <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
    <soapenv:Body>
      <leg:GetLegacyPlansRequest><leg:personalNumber>#(pnr)</leg:personalNumber></leg:GetLegacyPlansRequest>
    </soapenv:Body>
  </soapenv:Envelope>
  """
When soap action 'GetLegacyPlans'
Then status 200
And match /Envelope/Body/GetLegacyPlansResponse/count == '2'
```

- `soap action 'X'` pone la cabecera SOAPAction, el método POST y el Content-Type `text/xml`.
- `#(pnr)` funciona también dentro de XML.
- Las rutas XPath (`/Envelope/Body/...`) ignoran los prefijos de espacio de nombres (`soapenv:`, `leg:`).
- En XML **todo es texto**: el recuento es `'2'`, no `2`.
- `karate.xmlPath(response, '//plan[1]/planNumber')` saca un valor para usarlo después.

## La norma

1. Un 500 en SOAP no significa "caída": lee `faultcode` y `detail/errorCode`.
2. Valida el WSDL (`?wsdl`): es el contrato del servicio.
3. Solo las personas nacidas antes de 1975 tienen planes antiguos: prepara tus datos con la edad adecuada.

## El fundamento

Las empresas grandes no migran todo a la vez. Un SDET senior prueba REST, mensajería y SOAP en la misma suite.
