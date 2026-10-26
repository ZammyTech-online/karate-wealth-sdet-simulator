# ──────────────────────────────────────────────────────────────────────
# L23 · E02 · Mide la latencia
#
# Inyecta 300 ms de latencia y comprueba que la respuesta tardó al menos 280 ms. Completa el valor de la cabecera y la variable de Karate con el tiempo.
#
# Rellena cada ___ y comprueba con:  academia corregir L23
# Pista: El escenario se escribe clave=valor. Karate guarda el tiempo de la última respuesta en una variable response...
# ──────────────────────────────────────────────────────────────────────
@L23 @E02
Feature: L23 E02 · Mide la latencia

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Lento pero bien
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = ___
    When method get
    Then status 200
    And assert ___ >= 280
