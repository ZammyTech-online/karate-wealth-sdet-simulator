# Ayudante: el número nacional de una persona sintética de la edad que digas.
# Uso:  * def p = call read('_persona.feature') { edad: 66 }   →   p.pnr
@ignore
Feature: Ayudante · persona de una edad

  Scenario: Persona
    Given url personUrl
    And path '/api/v1/persons'
    And request { age: '#(edad)' }
    When method post
    Then status 200
    * def pnr = response.persons[0].nationalId
