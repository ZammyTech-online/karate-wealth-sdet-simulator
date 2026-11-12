# ──────────────────────────────────────────────────────────────────────
# L36 · E01 · Etiquetado semántico con @smoke
#
# Asegura la presencia del tag @smoke en un escenario crítico de salud de servicio.
#
# Rellena cada ___ y comprueba con:  academia corregir L36
# Pista: Usa la convención de tags de Karate con arroba
# ──────────────────────────────────────────────────────────────────────
@L36 @E01
Feature: L36 E01 · Etiquetado semántico con @smoke

  @smoke
  Scenario: E01 · Smoke test marcado
    * def ready = ___
    * match ready == ___
