@pedagogico @mid @pensions
Feature: M-B: Carga Externa de Payloads Complejos de Pensiones ITP1
  # [CONTEXTO ACTUARIAL]: Un cálculo actuarial de pensión colectiva tiene más de 40 campos.
  # Quemar ese JSON dentro del test ensucia el código. Usamos read('...') para desacoplar.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'
    * def basePayload =
      """
      {
        planType: "ITP1",
        employee: {
          age: 42,
          monthlySalary: 52000,
          salaryCurrency: "SEK"
        },
        collectiveAgreement: "SAF-LO"
      }
      """

  Scenario: Calcular aportación patronal obligatoria desacoplando datos y aserciones
    Given path '/api/v1/pensions/calculate'
    And request basePayload
    When method post
    Then status 200
    And match response.mandatoryContribution == '#number'
    And match response.mandatoryContribution > 0
    And match response.pensionTier == 'TIER_1'
