# Portal de desarrolladores de Nordvik

Generado automáticamente a partir de los servicios en marcha (perfil **nivel-3**). No lo edites a mano: ejecuta `node tools/docs-gen.mjs`.

| Sección | Dónde |
| --- | --- |
| Fichas de servicio | `docs/services/` |
| Contratos OpenAPI | `docs/openapi/` |
| Colecciones Bruno por equipo | `collections/` |
| Decisiones de arquitectura | `docs/adr/` |
| Flujos | `docs/flows/` |
| Informes de la noche | `docs/informes/` |

## A-utilities

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [uuid-service](services/uuid-service.md) | 6001 | Plataforma de datos de prueba | 0 | 6 | Genera identificadores únicos UUID v4 y v7, solos o en lote, y valida UUID. |
| [sequence-service](services/sequence-service.md) | 6002 | Plataforma de datos de prueba | 0 | 9 | Secuencias con nombre que devuelven identificadores correlativos con prefijo y relleno de ceros. |
| [clock-service](services/clock-service.md) | 6003 | Plataforma de datos de prueba | 0 | 8 | Reloj virtual del sistema. Todos los servicios preguntan aquí la hora; tus tests pueden viajar en el tiempo. |
| [random-seed-service](services/random-seed-service.md) | 6004 | Plataforma de datos de prueba | 0 | 8 | Números y elecciones aleatorias reproducibles a partir de una semilla, para repetir un fallo con los mismos datos. |
| [synthetic-person-service](services/synthetic-person-service.md) | 6005 | Plataforma de datos de prueba | 1 | 5 | Personas sintéticas nórdicas: nombre, número nacional válido (Luhn), edad según el reloj virtual, email, teléfono y dirección. |
| [address-service](services/address-service.md) | 6006 | Plataforma de datos de prueba | 1 | 6 | Direcciones sintéticas nórdicas con código postal coherente y validación de códigos postales. |
| [iban-service](services/iban-service.md) | 6007 | Plataforma de datos de prueba | 1 | 6 | Genera IBAN de prueba válidos de bancos ficticios y valida IBAN (longitud por país y dígitos de control mod 97). |
| [isin-service](services/isin-service.md) | 6008 | Plataforma de datos de prueba | 1 | 5 | Genera ISIN válidos y valida el dígito de control de cualquier ISIN. |
| [data-reset-service](services/data-reset-service.md) | 6009 | Plataforma de datos de prueba | 1 | 5 | Da un prefijo único por ejecución y borra todo lo creado con él en todos los servicios encendidos. |
| [fixture-loader](services/fixture-loader.md) | 6010 | Plataforma de datos de prueba | 1 | 5 | Escenarios precargados: cliente básico, cliente con cartera, cliente jubilado. Llama a 4-6 servicios por ti. |

## B-platform

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [api-gateway](services/api-gateway.md) | 6100 | Plataforma | 1 | 4 | Entrada única: enruta /{servicio}/... al microservicio, aplica cuotas, valida tokens activos y añade X-Correlation-Id. |
| [rate-limiter](services/rate-limiter.md) | 6101 | Plataforma | 1 | 6 | Cuotas por minuto por clave. El api-gateway le pregunta en cada petición; el límite se configura en config-service. |
| [config-service](services/config-service.md) | 6102 | Plataforma | 1 | 8 | Valores de configuración de cada servicio (horas de corte, límites, TTL) con versión e historial de cambios. |

## C-identity

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [iam-service](services/iam-service.md) | 6200 | Identidad | 1 | 8 | Login OAuth2 (password, client_credentials, refresh_token), tokens JWT de 5 minutos, introspección, revocación y bloqueo tras 5 intentos fallidos. |
| [session-service](services/session-service.md) | 6201 | Identidad | 1 | 8 | Sesiones activas por usuario: alta al iniciar sesión, caducidad por inactividad y cierre remoto. |
| [permission-service](services/permission-service.md) | 6202 | Identidad | 1 | 8 | Decide permisos por rol y propiedad del recurso: el cliente ve lo suyo, el asesor a sus clientes asignados, back office según su función. |

## D-customer

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [customer-registry](services/customer-registry.md) | 6300 | Clientes | 1 | 11 | Alta, consulta, modificación (con If-Match) y cierre de clientes. Enmascara el número nacional según el rol y publica un registro de cambios. |
| [contact-service](services/contact-service.md) | 6301 | Clientes | 1 | 8 | Medios de contacto del cliente con verificación por código: 3 intentos, caducidad de 10 minutos y bloqueo. |
| [customer-address-service](services/customer-address-service.md) | 6302 | Clientes | 1 | 6 | Domicilio actual e historial de domicilios del cliente; el código postal debe corresponder a la ciudad. |
| [preferences-service](services/preferences-service.md) | 6303 | Clientes | 1 | 5 | Preferencias de comunicación del cliente. Si nunca las guardó, devuelve los valores por defecto (isDefault=true). |
| [customer-search](services/customer-search.md) | 6304 | Clientes | 1 | 6 | Búsqueda rápida de clientes por nombre, número o email. Se actualiza cada segundo desde el registro de cambios (consistencia eventual). |

## I-market

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [product-catalog](services/product-catalog.md) | 6801 | Inversiones | 0 | 6 | Catálogo de fondos de Nordvik: categoría, divisa, riesgo SRI (1-7), estado y comisiones. |
| [market-data](services/market-data.md) | 6802 | Inversiones | 0 | 6 | Precios NAV diarios con 4 decimales, historial y último precio publicado (con ETag). |
| [fx-rates](services/fx-rates.md) | 6803 | Tesorería | 0 | 7 | Tipos de cambio diarios entre SEK, EUR, NOK, DKK, USD y GBP, y conversión de importes con redondeo bancario. |
| [calendar-service](services/calendar-service.md) | 6804 | Plataforma | 0 | 9 | Calendario bursátil de XSTO, XOSL, XCSE y XHEL: festivos, días hábiles, T+N y hora de corte de las 15:00. |
| [fund-factsheet](services/fund-factsheet.md) | 6805 | Inversiones | 1 | 4 | Ficha comercial del fondo por idioma: objetivo, explicación del riesgo, comisiones y rentabilidad a 1, 3 y 12 meses. |
| [fee-engine](services/fee-engine.md) | 6806 | Inversiones | 1 | 4 | Calcula la comisión de entrada (BUY), de salida (SELL) o el coste anual de gestión proyectado (HOLD) de un importe en un fondo. |
| [benchmark-service](services/benchmark-service.md) | 6807 | Inversiones | 1 | 7 | Índices de referencia NVX con un valor por día hábil, rentabilidad entre fechas y comparación fondo frente a índice. |

## J-investment

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [portfolio-service](services/portfolio-service.md) | 6900 | Inversiones | 1 | 8 | Carteras (ISK, KF, DEPOT) con asignación objetivo por fondo. Valida fondos con product-catalog y permisos con permission-service. |
| [allocation-engine](services/allocation-engine.md) | 6901 | Inversiones | 1 | 4 | Reglas de asignación: porcentaje en renta variable según riesgo, tope por horizonte corto y sustitución ESG. Siempre suma 100. |

**30 servicios · 196 endpoints documentados.**
