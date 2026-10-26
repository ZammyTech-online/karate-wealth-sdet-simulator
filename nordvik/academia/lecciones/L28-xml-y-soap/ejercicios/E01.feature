# ──────────────────────────────────────────────────────────────────────
# L28 · E01 · Tu primera petición SOAP
#
# Pide los planes de una persona de 66 años. Completa la palabra clave que manda la acción SOAP y el estado.
#
# Rellena cada ___ y comprueba con:  academia corregir L28
# Pista: Karate tiene un paso especial para SOAP: soap action 'Nombre'. Si todo va bien, 200.
# ──────────────────────────────────────────────────────────────────────
@L28 @E01
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
    When ___ 'GetLegacyPlans'
    Then status ___
