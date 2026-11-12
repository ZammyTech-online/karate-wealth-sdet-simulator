@ignore
Feature: Servidor Mock de Prueba para Fallos y Aprobaciones Actuariales

  Scenario: pathMatches('/api/v1/mock/actuary') && methodIs('get')
    * def response = { status: 'APPROVED', riskScore: 12.5, approvedBy: 'Robot-Senior' }
    * def responseStatus = 200

  Scenario: pathMatches('/api/v1/mock/actuary') && methodIs('post')
    * def response = { transactionId: 'TX-999', result: 'COMMITTED' }
    * def responseStatus = 201

  Scenario: pathMatches('/api/v1/mock/simulate-timeout')
    * karate.abort()
