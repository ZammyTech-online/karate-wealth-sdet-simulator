# ──────────────────────────────────────────────────────────────────────
# L06 · E03 · La forma de un error
#
# Pide un fondo que no existe y comprueba dos campos del formato RFC 9457: title y status.
#
# Rellena cada ___ y comprueba con:  academia corregir L06
# Pista: Ejecuta con print response o mira la teoría: title es una frase corta en español.
# ──────────────────────────────────────────────────────────────────────
@L06 @E03
Feature: L06 E03 · La forma de un error

  Scenario: Problem Details de un fondo inexistente
    Given url catalogUrl
    And path '/api/v1/funds/SE9999999999'
    When method get
    Then status 404
    And match response.code == 'FUND_NOT_FOUND'
    And match response.title == ___
    And match response.status == ___
