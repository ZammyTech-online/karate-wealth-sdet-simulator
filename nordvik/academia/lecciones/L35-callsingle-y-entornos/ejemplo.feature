@L35 @ejemplo
Feature: L35 · Ejemplo de karate.callSingle

  Scenario: Reutilizar token globalmente
    * def session = karate.callSingle('this:_mock_auth.feature')
    * match session.token == '#present'
    * match session.role == 'SYSTEM_ADMIN'
