# ──────────────────────────────────────────────────────────────────────
# L28 · E03 · Un Fault no es una caída
#
# Con un número nacional inválido el servicio responde un SOAP Fault. Completa el estado HTTP y el código de error del detail.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L28 @E03 @solucion
Feature: L28 E03 · Un Fault no es una caída

  Scenario: Número inválido
    * def pnr = '19600505-000X'
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
    Then status 500
    And match /Envelope/Body/Fault/faultcode == 'soapenv:Client'
    And match /Envelope/Body/Fault/detail/errorCode == 'INVALID_PNR'
