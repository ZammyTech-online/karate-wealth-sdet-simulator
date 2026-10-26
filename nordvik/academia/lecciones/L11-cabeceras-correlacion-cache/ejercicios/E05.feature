# ──────────────────────────────────────────────────────────────────────
# L11 · E05 · El tipo de contenido de un error
#
# Los errores no son application/json normal: tienen su propio tipo de contenido. Compruébalo.
#
# Rellena cada ___ y comprueba con:  academia corregir L11
# Pista: Es 'application/' seguido del nombre del formato RFC 9457 y '+json'.
# ──────────────────────────────────────────────────────────────────────
@L11 @E05
Feature: L11 E05 · El tipo de contenido de un error

  Scenario: Los errores tienen su propio Content-Type
    Given url catalogUrl
    And path '/api/v1/funds/hola'
    When method get
    Then status 400
    And match responseHeaders['Content-Type'][0] contains ___
