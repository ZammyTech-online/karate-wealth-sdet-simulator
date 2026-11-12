# ──────────────────────────────────────────────────────────────────────
# L36 · E01 · Etiquetado semántico con @smoke
#
# Asegura la presencia del tag @smoke en un escenario crítico de salud de servicio.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Los tags @smoke se ejecutan en cada commit en los pipelines de GitHub Actions.
# ──────────────────────────────────────────────────────────────────────
@L36 @E01 @solucion
Feature: L36 E01 · Etiquetado semántico con @smoke

  @smoke
  Scenario: E01 · Smoke test marcado
    * def ready = true
    * match ready == true
