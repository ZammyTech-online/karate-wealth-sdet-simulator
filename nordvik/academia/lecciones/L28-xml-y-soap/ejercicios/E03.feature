# ──────────────────────────────────────────────────────────────────────
# L28 · E03 · Un Fault no es una caída
#
# Con un número nacional inválido el servicio responde un SOAP Fault. Completa el estado HTTP y el código de error del detail.
#
# Rellena cada ___ y comprueba con:  academia corregir L28
# Pista: SOAP 1.1 manda los Faults con HTTP 500. El código está en detail/errorCode.
# ──────────────────────────────────────────────────────────────────────
@L28 @E03
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
    Then status ___
    And match /Envelope/Body/Fault/faultcode == 'soapenv:Client'
    And match /Envelope/Body/Fault/detail/errorCode == ___
