# ──────────────────────────────────────────────────────────────────────
# L39 · E03 · Estrategia de cache de dependencias Maven
#
# Indica la clave de cache para acelerar los runners en GitHub Actions.
#
# Rellena cada ___ y comprueba con:  academia corregir L39
# Pista: maven-deps-${{ hashFiles('**/pom.xml') }}
# ──────────────────────────────────────────────────────────────────────
@L39 @E03
Feature: L39 E03 · Estrategia de cache de dependencias Maven

  Scenario: E03 · Cache key
    * def cacheKey = ___
    * match cacheKey == ___
