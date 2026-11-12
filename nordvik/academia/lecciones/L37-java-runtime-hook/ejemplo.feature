@L37 @ejemplo
Feature: L37 · Configuración de Hooks de Ciclo de Vida

  Scenario: Hook de escenario ejecutado
    * configure afterScenario = function(){ karate.log('Scenario completado con éxito') }
    * def x = 100
    * match x == 100
