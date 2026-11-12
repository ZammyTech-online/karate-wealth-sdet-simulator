# ──────────────────────────────────────────────────────────────────────
# L37 · E04 · Medición de tiempo transcurrido con karate.info
#
# Verifica que karate.info contenga las claves del feature en ejecución.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Facilita la ubicación de recursos relativos dentro del proyecto.
# ──────────────────────────────────────────────────────────────────────
@L37 @E04 @solucion
Feature: L37 E04 · Medición de tiempo transcurrido con karate.info

  Scenario: E04 · Directorio del feature
    * def info = karate.info
    * match info.featureDir == '#present'
