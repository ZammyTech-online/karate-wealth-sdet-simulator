# ──────────────────────────────────────────────────────────────────────
# L28 · E04 · Sin SOAPAction
#
# Manda el mismo sobre con method post (sin soap action). Completa la cabecera de tipo de contenido y el código de error.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L28 @E04 @solucion
Feature: L28 E04 · Sin SOAPAction

  Scenario: Falta la acción
    * def p = call read('_persona.feature') { edad: 66 }
    * def pnr = p.pnr
    Given url soapUrl
    And path '/soap/LegacyPensionService'
    And header Content-Type = 'text/xml'
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
    When method post
    Then status 500
    And match /Envelope/Body/Fault/detail/errorCode == 'NO_SOAPACTION'
