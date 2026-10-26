# ──────────────────────────────────────────────────────────────────────
# L23 · E03 · Un escenario mal escrito
#
# status=200 no es un fallo: Nordvik lo rechaza. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L23
# Pista: Es un error del que pide. El código habla de un escenario de caos no válido.
# ──────────────────────────────────────────────────────────────────────
@L23 @E03
Feature: L23 E03 · Un escenario mal escrito

  Scenario: Caos inválido
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'status=200'
    When method get
    Then status ___
    And match response.code == ___
