@pedagogico @staff @time @audit
Feature: T-D: Aceleración Temporal con Reloj Virtual para Cierres Contables y Devengos
  # [CONTEXTO ACTUARIAL]: El cálculo del impuesto sueco sobre cuentas ISK ocurre cada trimestre.
  # En lugar de esperar 90 días, adelantamos el reloj virtual de Nordvik para verificar el cargo fiscal.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario: Adelantar el reloj al 31 de diciembre y certificar el devengo fiscal automático
    # Paso 1: Consultamos la fecha actual del reloj virtual
    Given path '/api/v1/clock'
    When method get
    Then status 200
    * def originalDate = response.virtualTime

    # Paso 2: Adelantamos el reloj al cierre fiscal (31 de diciembre)
    Given path '/api/v1/clock/fast-forward'
    And request { days: 90, targetEvent: 'YEAR_END_TAX_CLOSE' }
    When method post
    Then status 200
    And match response.virtualTime contains '12-31'

    # Paso 3: Verificamos que el motor fiscal calculó el devengo del impuesto ISK
    Given path '/api/v1/accounts/ACC-ISK-100/tax-accruals'
    When method get
    Then status 200
    And match response.iskTaxAmount == '#number'
    And match response.taxYear == 2026
    And match response.status == 'ACCRUED'
