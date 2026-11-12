@L34 @ejemplo
Feature: L34 · Ejemplo de servidor Mock con karate.start

  Scenario: Levantar mock y consumir endpoint simulado
    * def mock = karate.start({ mock: 'this:_mock_demo.feature', port: 0 })
    * def mockPort = mock.port
    * url 'http://localhost:' + mockPort
    * path '/api/v1/mock/actuary'
    * method get
    * status 200
    * match response == { status: 'APPROVED', riskScore: 12.5, approvedBy: 'Robot-Senior' }
    * mock.stop()
