# ──────────────────────────────────────────────────────────────────────
# L08 · E06 · Campos opcionales
#
# Validamos un texto que no es un UUID: version llega como null. Usa el marcador de número opcional.
#
# Rellena cada ___ y comprueba con:  academia corregir L08
# Pista: Un marcador con doble # acepta también null o ausente.
# ──────────────────────────────────────────────────────────────────────
@L08 @E06
Feature: L08 E06 · Campos opcionales

  Scenario: Validar algo que no es un UUID
    Given url uuidUrl
    And path '/api/v1/uuids/validate'
    And request { value: 'no-soy-un-uuid' }
    When method post
    Then status 200
    And match response == { value: '#string', valid: false, version: ___, variant: '##string', reason: '#string' }
