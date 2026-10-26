# Ayudante: crea un cliente nuevo de asesor.erik, le ingresa dinero de verdad (OCR + abono en el banco simulado)
# y le abre una cartera. Devuelve customerId, portfolioId, email y los tokens de erik y olof.
# Uso:  * def cliente = callonce read('_cliente-con-dinero.feature') { importe: '1000.00' }
@ignore
Feature: Ayudante · cliente nuevo con dinero

  Scenario: Cliente con dinero
    # 'this:' busca el fichero junto a ESTE ayudante, no junto a la feature que lo llama
    * def erik = call read('this:_login.feature') usuarios.erik
    * def olof = call read('this:_login.feature') usuarios.olof

    # 1 · una persona sintética mayor de edad
    Given url personUrl
    And path '/api/v1/persons'
    And request { minAge: 25, maxAge: 60 }
    When method post
    Then status 200
    * def p = response.persons[0]

    # 2 · el asesor da de alta al cliente
    Given url customerUrl
    And path '/api/v1/customers'
    And headers erik.auth
    And request { firstName: '#(p.firstName)', lastName: '#(p.lastName)', nationalId: '#(p.nationalId)', email: '#(p.email)' }
    When method post
    Then status 201
    * def customerId = response.id
    * def email = p.email

    # 3 · operaciones abre un ingreso: Nordvik da una referencia OCR
    Given url paymentsUrl
    And path '/api/v1/deposits'
    And headers olof.auth
    And request { customerId: '#(customerId)', expectedAmount: '#(importe)' }
    When method post
    Then status 201
    * def deposito = response

    # 4 · el cliente transfiere desde su banco (simulador NordPay) con esa referencia
    Given url ibanUrl
    And path '/api/v1/ibans'
    And request { country: 'SE' }
    When method post
    Then status 201
    * def ibanCliente = response.electronic
    Given url nordpaySimUrl
    And path '/__admin/incoming-credits'
    And request { debtorIban: '#(ibanCliente)', debtorName: 'Cliente Academia', amount: '#(importe)', reference: '#(deposito.reference)' }
    When method post
    Then status 201

    # 5 · el aviso del banco llega por webhook: esperamos a que el ingreso quede RECEIVED
    * configure retry = { count: 40, interval: 500 }
    Given url paymentsUrl
    And path '/api/v1/payments', deposito.id
    And headers olof.auth
    And retry until response.status == 'RECEIVED'
    When method get
    Then status 200

    # 6 · una cartera ISK 100 % en un fondo
    Given url portfolioUrl
    And path '/api/v1/portfolios'
    And headers erik.auth
    And request { customerId: '#(customerId)', name: '#("Cartera " + customerId.substring(0, 6))', type: 'ISK', targetAllocation: [{ isin: 'SE0000000003', percent: 100 }] }
    When method post
    Then status 201
    * def portfolioId = response.id
