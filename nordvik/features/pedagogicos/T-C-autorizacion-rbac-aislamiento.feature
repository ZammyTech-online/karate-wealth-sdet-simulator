@pedagogico @staff @security @rbac
Feature: T-C: Matriz de Control de Acceso Basado en Roles (RBAC) para Asesores y Clientes
  # [CONTEXTO ACTUARIAL]: La Ley de Distribución de Seguros (IDD) prohíbe que un asesor
  # modifique la cláusula de beneficiarios de un seguro de vida sin la firma expresa del tomador.

  Background:
    * url 'http://localhost:5125'
    * header Content-Type = 'application/json'

  Scenario Outline: Validar matriz de permisos sobre modificación de póliza de seguro
    Given path '/api/v1/insurance/POL-992/beneficiary'
    And header Authorization = 'Bearer ' + '<tokenRole>'
    And request { primaryBeneficiary: 'Carl von Linne', percentage: 100 }
    When method put
    Then status <expectedStatus>

    Examples:
      | tokenRole       | expectedStatus |
      | POLICY_HOLDER   | 200            |
      | JUNIOR_ADVISOR  | 403            |
      | AUDITOR_READ    | 403            |
      | CHIEF_ACTUARY   | 200            |
