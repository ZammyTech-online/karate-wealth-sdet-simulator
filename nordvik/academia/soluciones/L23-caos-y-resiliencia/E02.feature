# ──────────────────────────────────────────────────────────────────────
# L23 · E02 · Mide la latencia
#
# Inyecta 300 ms de latencia y comprueba que la respuesta tardó al menos 280 ms. Completa el valor de la cabecera y la variable de Karate con el tiempo.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L23 @E02 @solucion
Feature: L23 E02 · Mide la latencia

  Background:
    Given url chaosUrl
    And path '/api/v1/reset'
    When method post
    Then status 200

  Scenario: Lento pero bien
    Given url uuidUrl
    And path '/health'
    And header X-Chaos-Scenario = 'latency=300'
    When method get
    Then status 200
    And assert responseTime >= 280
