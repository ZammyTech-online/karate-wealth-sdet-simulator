@L38 @ejemplo
Feature: L38 · Definición de SLA de Rendimiento

  Scenario: Comprobar tiempo de respuesta bajo SLA
    * def maxMs = 250
    * def latency = 120
    * assert latency < maxMs
