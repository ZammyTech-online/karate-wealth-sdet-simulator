# ──────────────────────────────────────────────────────────────────────
# L35 · E05 · Configuración multientorno con karate.env
#
# Valida que karate.env esté definido o adopte un valor por defecto seguro como 'dev'.
#
# Rellena cada ___ y comprueba con:  academia corregir L35
# Pista: karate.env || 'dev'
# ──────────────────────────────────────────────────────────────────────
@L35 @E05
Feature: L35 E05 · Configuración multientorno con karate.env

  Scenario: E05 · Entorno seguro
    * def env = ___
    * match env == ___
