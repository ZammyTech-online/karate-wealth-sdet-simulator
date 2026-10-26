# F-05 · Segundo factor y step-up

1. `mfa-service` (6204) genera un código de 6 dígitos y lo manda por `notification-service` (6600). Guarda **solo el hash**.
   El código caduca a los 5 minutos y admite 3 intentos.
2. En pruebas, el mensaje se lee en el buzón simulado: `GET /api/v1/mailbox?to=<email o teléfono>` (más reciente primero).
3. `step-up-service` (6205) cambia el código (o una firma NordID) por un token con `acr=high` que dura 5 minutos.
4. Cuando quien opera es el **cliente**, `payments-gateway` lo exige para retiradas por encima del umbral configurado y
   `bank-account-verification` para añadir una cuenta de destino: con un token normal responden 403 `STEP_UP_REQUIRED`.

## Dónde se prueba

`ExternalIntegrationTests.cs` (`Mfa_code_arrives_in_the_mailbox_and_is_single_use` y siguientes).
