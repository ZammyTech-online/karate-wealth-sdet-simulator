# ──────────────────────────────────────────────────────────────────────
# L24 · E02 · Un error de tecleo no te salva
#
# 'Viktor Draganow' (con w) también salta. Completa el status esperado.
#
# Rellena cada ___ y comprueba con:  academia corregir L24
# Pista: La coincidencia es difusa: se parece mucho, así que alguien tiene que revisarlo.
# ──────────────────────────────────────────────────────────────────────
@L24 @E02
Feature: L24 E02 · Un error de tecleo no te salva

  Scenario: Casi el mismo nombre
    * def karin = call read('_login.feature') usuarios.karin
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganow', birthDate: '1961-04-12' }
    When method post
    Then status 201
    And match response.status == ___
