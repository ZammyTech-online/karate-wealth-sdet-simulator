@L36 @ejemplo
Feature: L36 · Tags y preparación de ejecución paralela

  @smoke @fast
  Scenario: Escenario rápido de comprobación
    * def status = 'HEALTHY'
    * match status == 'HEALTHY'

  @regression
  Scenario: Escenario de regresión actuarial
    * def balance = 50000
    * assert balance > 10000
