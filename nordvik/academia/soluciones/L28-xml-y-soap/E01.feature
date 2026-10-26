# ──────────────────────────────────────────────────────────────────────
# L28 · E01 · Tu primera petición SOAP
#
# Pide los planes de una persona de 66 años. Completa la palabra clave que manda la acción SOAP y el estado.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L28 @E01 @solucion
Feature: L28 E01 · Tu primera petición SOAP

  Scenario: Listar
    * def p = call read('_persona.feature') { edad: 66 }
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
