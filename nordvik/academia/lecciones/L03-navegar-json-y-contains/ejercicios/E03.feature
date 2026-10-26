# ──────────────────────────────────────────────────────────────────────
# L03 · E03 · Comprobar solo una parte
#
# Solo nos importan dos campos del fondo: que sea de renta variable (EQUITY) y de riesgo 4.
# Usa la variante de match que permite que sobren campos.
#
# Rellena cada ___ y comprueba con:  academia corregir L03
# Pista: Con == tendrías que escribir el objeto entero. Hay una palabra que significa 'contiene'.
# ──────────────────────────────────────────────────────────────────────
@L03 @E03
Feature: L03 E03 · Comprobar solo una parte

  Scenario: Categoría y riesgo del fondo global
    Given url catalogUrl
    And path '/api/v1/funds', 'SE0000000001'
    When method get
    Then status 200
    And match response ___ { category: 'EQUITY', sri: 4 }
