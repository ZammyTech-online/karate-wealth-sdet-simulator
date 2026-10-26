# ──────────────────────────────────────────────────────────────────────
# L00 · E01 · El método para leer
#
# Queremos LEER la lista de fondos. ¿Qué método HTTP se usa para leer?
#
# Rellena cada ___ y comprueba con:  academia corregir L00
# Pista: Leer en HTTP es el método más común de todos. Se escribe en minúsculas en Karate.
# ──────────────────────────────────────────────────────────────────────
@L00 @E01
Feature: L00 E01 · El método para leer

  Scenario: Leer la lista de fondos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method ___
    Then status 200
