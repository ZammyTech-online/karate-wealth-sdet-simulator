# ──────────────────────────────────────────────────────────────────────
# L35 · E05 · Configuración multientorno con karate.env
#
# Valida que karate.env esté definido o adopte un valor por defecto seguro como 'dev'.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Garantiza que la suite pueda ejecutarse en local, dev, staging o producción.
# ──────────────────────────────────────────────────────────────────────
@L35 @E05 @solucion
Feature: L35 E05 · Configuración multientorno con karate.env

  Scenario: E05 · Entorno seguro
    * def env = karate.env || 'dev'
    * match env == '#string'
