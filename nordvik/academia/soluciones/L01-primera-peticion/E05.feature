# ──────────────────────────────────────────────────────────────────────
# L01 · E05 · ¿Está vivo el servicio?
#
# Todos los servicios de Nordvik tienen una ruta que dice si están funcionando.
# Pídesela a calendar-service.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L01 @E05 @solucion
Feature: L01 E05 · ¿Está vivo el servicio?

  Scenario: Salud de calendar-service
    Given url calendarUrl
    And path '/health'
    When method get
    Then status 200
    And match response.status == 'UP'
