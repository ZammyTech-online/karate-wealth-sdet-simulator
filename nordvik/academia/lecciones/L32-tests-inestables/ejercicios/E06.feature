# ──────────────────────────────────────────────────────────────────────
# L32 · E06 · Nombres repetidos
#
# Dos resultados con el mismo nombre en una ejecución no se aceptan. Completa estado y código.
#
# Rellena cada ___ y comprueba con:  academia corregir L32
# Pista: El cuerpo es válido como JSON, pero no tiene sentido: 422. El código habla de nombres duplicados.
# ──────────────────────────────────────────────────────────────────────
@L32 @E06
Feature: L32 E06 · Nombres repetidos

  Scenario: Duplicados
    Given url testInsightsUrl
    And path '/api/v1/runs'
    And request { suite: 'dup', results: [ { name: 'X', status: 'passed' }, { name: 'X', status: 'failed' } ] }
    When method post
    Then status ___
    And match response.code == ___
