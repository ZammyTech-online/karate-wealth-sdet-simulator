# Ejemplo resuelto de L28. Ejecútalo con:  academia ejemplo L28
@ejemplo
Feature: L28 · Planes antiguos por SOAP

  Scenario: Listar los planes de una persona de 66 años
    * def p = call read('ejercicios/_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And request
      """
      <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:leg="urn:nordvik:legacy:pensions:v1">
        <soapenv:Body>
          <leg:GetLegacyPlansRequest>
            <leg:personalNumber>#(pnr)</leg:personalNumber>
          </leg:GetLegacyPlansRequest>
        </soapenv:Body>
      </soapenv:Envelope>
      """
    When soap action 'GetLegacyPlans'
    Then status 200
    And match /Envelope/Body/GetLegacyPlansResponse/count == '#notnull'
    * def numero = karate.xmlPath(response, '//plan[1]/planNumber')
    * match numero == '#regex LP-\\d{4}-[0-9A-F]{4}'
