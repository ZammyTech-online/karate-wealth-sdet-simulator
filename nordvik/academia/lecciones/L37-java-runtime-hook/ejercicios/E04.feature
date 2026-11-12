# ──────────────────────────────────────────────────────────────────────
# L37 · E04 · Medición de tiempo transcurrido con karate.info
#
# Verifica que karate.info contenga las claves del feature en ejecución.
#
# Rellena cada ___ y comprueba con:  academia corregir L37
# Pista: karate.info
# ──────────────────────────────────────────────────────────────────────
@L37 @E04
Feature: L37 E04 · Medición de tiempo transcurrido con karate.info

  Scenario: E04 · Directorio del feature
    * def info = ___
    * match info.featureDir == ___
