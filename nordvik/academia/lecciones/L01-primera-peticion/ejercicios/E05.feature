# ──────────────────────────────────────────────────────────────────────
# L01 · E05 · ¿Está vivo el servicio?
#
# Todos los servicios de Nordvik tienen una ruta que dice si están funcionando.
# Pídesela a calendar-service.
#
# Rellena cada ___ y comprueba con:  academia corregir L01
# Pista: Es una ruta corta, sin /api/v1. Mírala en Swagger, en el grupo 'Plataforma'.
# ──────────────────────────────────────────────────────────────────────
@L01 @E05
Feature: L01 E05 · ¿Está vivo el servicio?

  Scenario: Salud de calendar-service
    Given url calendarUrl
    And path ___
    When method get
    Then status 200
    And match response.status == 'UP'
