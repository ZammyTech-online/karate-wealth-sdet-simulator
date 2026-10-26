# ──────────────────────────────────────────────────────────────────────
# L11 · E05 · El tipo de contenido de un error
#
# Los errores no son application/json normal: tienen su propio tipo de contenido. Compruébalo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L11 @E05 @solucion
Feature: L11 E05 · El tipo de contenido de un error

  Scenario: Los errores tienen su propio Content-Type
    Given url catalogUrl
    And path '/api/v1/funds/hola'
    When method get
    Then status 400
    And match responseHeaders['Content-Type'][0] contains 'application/problem+json'
