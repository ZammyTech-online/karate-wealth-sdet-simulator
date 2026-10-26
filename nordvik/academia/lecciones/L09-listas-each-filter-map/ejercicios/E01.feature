# ──────────────────────────────────────────────────────────────────────
# L09 · E01 · Cada elemento cumple
#
# Filtramos por EQUITY. Comprueba que cada fondo devuelto es de esa categoría.
#
# Rellena cada ___ y comprueba con:  academia corregir L09
# Pista: match each ... ___ { category: 'EQUITY' }: falta la palabra que permite que sobren campos.
# ──────────────────────────────────────────────────────────────────────
@L09 @E01
Feature: L09 E01 · Cada elemento cumple

  Scenario: El filtro de categoría funciona
    Given url catalogUrl
    And path '/api/v1/funds'
    And param category = 'EQUITY'
    When method get
    Then status 200
    And match each response.items ___ { category: 'EQUITY' }
