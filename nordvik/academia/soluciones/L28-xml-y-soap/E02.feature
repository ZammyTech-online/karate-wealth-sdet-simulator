# ──────────────────────────────────────────────────────────────────────
# L28 · E02 · En XML todo es texto
#
# Una persona de 36 años no tiene planes antiguos. Completa la ruta XPath del recuento y el valor esperado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L28 @E02 @solucion
Feature: L28 E02 · En XML todo es texto

  Scenario: Sin planes
    * def p = call read('_persona.feature') { edad: 36 }
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
    And match /Envelope/Body/GetLegacyPlansResponse/count == '0'
