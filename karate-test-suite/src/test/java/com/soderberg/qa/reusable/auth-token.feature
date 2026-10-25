@ignore
Feature: Reusable OAuth2 Token Generator for Wealth Advisors & Admins

  Scenario: Generate Bearer Token for Requested Role
    * url baseUrl
    * path '/api/v1/auth/token'
    * def isArgPresent = typeof __arg !== 'undefined' && __arg !== null
    * def reqUser = isArgPresent && __arg.username ? __arg.username : advisorUser
    * def reqPass = isArgPresent && __arg.password ? __arg.password : advisorPass
    * def reqTtl = isArgPresent && __arg.ttlMinutes ? parseInt(__arg.ttlMinutes) : 15

    * request { username: '#(reqUser)', password: '#(reqPass)', ttlMinutes: '#(reqTtl)' }
    * method post
    * status 200
    * def authToken = response.access_token
    * def authRole = response.role
    * karate.log('Generated token for role:', authRole, 'with length:', authToken.length)
