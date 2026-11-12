# ──────────────────────────────────────────────────────────────────────
# L39 · E03 · Estrategia de cache de dependencias Maven
#
# Indica la clave de cache para acelerar los runners en GitHub Actions.
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# Evita descargar Karate y dependencias Java en cada ejecución de PR.
# ──────────────────────────────────────────────────────────────────────
@L39 @E03 @solucion
Feature: L39 E03 · Estrategia de cache de dependencias Maven

  Scenario: E03 · Cache key
    * def cacheKey = 'maven-deps-pom-hash'
    * match cacheKey == 'maven-deps-pom-hash'
