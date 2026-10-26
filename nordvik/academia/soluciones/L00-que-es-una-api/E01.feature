# ──────────────────────────────────────────────────────────────────────
# L00 · E01 · El método para leer
#
# Queremos LEER la lista de fondos. ¿Qué método HTTP se usa para leer?
#
# SOLUCIÓN OFICIAL (pista 3). Léela, ciérrala y escríbela tú sin mirar.
# ──────────────────────────────────────────────────────────────────────
@L00 @E01 @solucion
Feature: L00 E01 · El método para leer

  Scenario: Leer la lista de fondos
    Given url catalogUrl
    And path '/api/v1/funds'
    When method get
    Then status 200
