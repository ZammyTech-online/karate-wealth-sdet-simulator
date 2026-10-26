# Ejemplo resuelto de L24. Ejecútalo con:  academia ejemplo L24
@ejemplo
Feature: L24 · Sanciones

  Background:
    * def karin = callonce read('ejercicios/_login.feature') usuarios.karin

  Scenario: Un nombre limpio
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Persona Corriente' }
    When method post
    Then status 201
    And match response contains { status: 'CLEAR', threshold: 85, matches: [] }

  Scenario: Coincidencia exacta y decisión de Cumplimiento
    Given url sanctionsUrl
    And path '/api/v1/screenings'
    And headers karin.auth
    And request { name: 'Viktor Draganov', birthDate: '1961-04-12' }
    When method post
    Then status 201
    And match response.status == 'POTENTIAL_MATCH'
    Given path '/api/v1/screenings', response.id, 'decision'
    And headers karin.auth
    And request { decision: 'TRUE_MATCH', reason: 'Mismo nombre y fecha de nacimiento' }
    When method post
    Then status 200
    And match response.status == 'CONFIRMED_MATCH'
