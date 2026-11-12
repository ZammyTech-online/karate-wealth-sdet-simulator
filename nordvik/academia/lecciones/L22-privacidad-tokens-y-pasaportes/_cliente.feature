# Ayudante: un cliente nuevo de asesor.erik. Devuelve customerId, nationalId y el login de erik.
# Uso:  * def c = call read('_cliente.feature')
@ignore
Feature: Ayudante · cliente nuevo

  Scenario: Cliente nuevo
    * def erik = call read('this:_login.feature') usuarios.erik
    Given url personUrl
    And path '/api/v1/persons'
    And request { minAge: 25, maxAge: 60 }
    When method post
    Then status 200
    * def p = response.persons[0]
    Given url customerUrl
    And path '/api/v1/customers'
    And headers erik.auth
    And request { firstName: '#(p.firstName)', lastName: '#(p.lastName)', nationalId: '#(p.nationalId)', email: '#(p.email)' }
    When method post
    Then status 201
    * def customerId = response.id
    * def nationalId = p.nationalId
