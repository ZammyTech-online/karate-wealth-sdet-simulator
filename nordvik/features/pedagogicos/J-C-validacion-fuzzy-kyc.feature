@pedagogico @junior @kyc
Feature: J-C: Validación de Tipos Fuzzy & Marcadores de Karate en Expedientes KYC
  # [CONTEXTO ACTUARIAL]: La regulación sueca exige validar la identidad nacional (Personnummer).
  # En pruebas no fijamos datos quemados (hardcodeados), sino patrones regulatorios (#regex).

  Background:
    * url 'http://localhost:5125'
    * header Accept = 'application/json'

  Scenario: Verificar esquema del cliente con marcadores fuzzy (#string, #number, #regex)
    Given path '/api/v1/customers/C-9011'
    When method get
    Then status 200
    And match response ==
      """
      {
        id: '#string',
        fullName: '#string',
        personalNumber: '#regex ^(19|20)\d{6}-\d{4}$',
        riskCategory: '#? _ == "LOW" || _ == "MEDIUM" || _ == "HIGH"',
        kycVerified: '#boolean',
        tier: '#number'
      }
      """
