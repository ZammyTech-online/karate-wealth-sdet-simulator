# Portal de desarrolladores de Nordvik

Generado automáticamente a partir de los servicios en marcha (perfil **nivel-6**). No lo edites a mano: ejecuta `node tools/docs-gen.mjs`.

| Sección | Dónde |
| --- | --- |
| Fichas de servicio | `docs/services/` |
| Catálogo de eventos | `docs/events.md` |
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
| [hash-service](services/hash-service.md) | 6011 | Plataforma de datos de prueba | 2 | 7 | Calcula el JSON canónico y su SHA-256 (como contentHash), SHA-256 de texto y HMAC-SHA256 de webhooks. |
| [text-factory](services/text-factory.md) | 6012 | Plataforma de datos de prueba | 2 | 6 | Mensajes de cliente con PII opcional (personnummer, IBAN, teléfono) y guiones de llamada V1-V5 con marcas de tiempo. |
| [file-factory](services/file-factory.md) | 6013 | Plataforma de datos de prueba | 2 | 10 | Genera ficheros de prueba: PDF, EICAR, tipo falso, ficheros grandes, anexos JSON y CSV de empleador con errores controlados (reproducibles con seed). |

## B-platform

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [api-gateway](services/api-gateway.md) | 6100 | Plataforma | 1 | 4 | Entrada única: enruta /{servicio}/... al microservicio, aplica cuotas, valida tokens activos y añade X-Correlation-Id. |
| [rate-limiter](services/rate-limiter.md) | 6101 | Plataforma | 1 | 6 | Cuotas por minuto por clave. El api-gateway le pregunta en cada petición; el límite se configura en config-service. |
| [config-service](services/config-service.md) | 6102 | Plataforma | 1 | 8 | Valores de configuración de cada servicio (horas de corte, límites, TTL) con versión e historial de cambios. |
| [idempotency-store](services/idempotency-store.md) | 6103 | Plataforma | 2 | 7 | Almacén de claves de idempotencia con el hash del cuerpo y la respuesta guardada. Lo usan order-management y payments-gateway. |
| [bff-web](services/bff-web.md) | 6104 | Canales digitales | 2 | 4 | API a medida de la web del cliente: panel en una llamada (cliente, efectivo, carteras valoradas, órdenes recientes, notificaciones). |
| [bff-mobile](services/bff-mobile.md) | 6105 | Canales digitales | 2 | 5 | API de la app: inicio compacto, órdenes paginadas por cursor y ?fields= para pedir solo algunos campos. |
| [bff-advisor](services/bff-advisor.md) | 6106 | Canales digitales | 2 | 6 | API del escritorio del asesor: sus clientes con carteras y valor, ficha del cliente (vía composition-service) y alertas de desviación. |
| [composition-service](services/composition-service.md) | 6107 | Plataforma | 2 | 17 | Composición de APIs: contrato + anexo (C1), posiciones con fondo (C2), extracto mensual (C3), ficha 360 (C4), contrato marco con fusión profunda (C5), mandato (C6). |
| [correlation-tracer](services/correlation-tracer.md) | 6108 | Plataforma | 2 | 4 | Reconstruye la cadena de llamadas de un correlationId a partir de los logs (servicio, ruta, estado, duración y anidamiento). |

## C-identity

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [iam-service](services/iam-service.md) | 6200 | Identidad | 1 | 8 | Login OAuth2 (password, client_credentials, refresh_token), tokens JWT de 5 minutos, introspección, revocación y bloqueo tras 5 intentos fallidos. |
| [session-service](services/session-service.md) | 6201 | Identidad | 1 | 8 | Sesiones activas por usuario: alta al iniciar sesión, caducidad por inactividad y cierre remoto. |
| [permission-service](services/permission-service.md) | 6202 | Identidad | 1 | 8 | Decide permisos por rol y propiedad del recurso: el cliente ve lo suyo, el asesor a sus clientes asignados, back office según su función. |
| [eid-broker](services/eid-broker.md) | 6203 | Identidad | 2 | 7 | Autenticación y firma con NordID (E1): orderRef, sondeo, caducidad a 3 min y borrado del resultado a los 10 min (dato efímero). |
| [mfa-service](services/mfa-service.md) | 6204 | Identidad | 2 | 6 | Segundo factor: código de 6 dígitos por SMS o email (vía notification-service), 5 min, 3 intentos. Solo se guarda el hash del código. |
| [step-up-service](services/step-up-service.md) | 6205 | Identidad | 2 | 6 | Re-autenticación (acr=high) con NordID o MFA. El token resultante dura 5 minutos y lo exigen payments-gateway y bank-account-verification. |

## D-customer

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [customer-registry](services/customer-registry.md) | 6300 | Clientes | 1 | 11 | Alta, consulta, modificación (con If-Match) y cierre de clientes. Enmascara el número nacional según el rol y publica un registro de cambios. |
| [contact-service](services/contact-service.md) | 6301 | Clientes | 1 | 8 | Medios de contacto del cliente con verificación por código: 3 intentos, caducidad de 10 minutos y bloqueo. |
| [customer-address-service](services/customer-address-service.md) | 6302 | Clientes | 1 | 7 | Domicilio actual e historial de domicilios del cliente; el código postal debe corresponder a la ciudad. |
| [preferences-service](services/preferences-service.md) | 6303 | Clientes | 1 | 6 | Preferencias de comunicación del cliente. Si nunca las guardó, devuelve los valores por defecto (isDefault=true). |
| [customer-search](services/customer-search.md) | 6304 | Clientes | 1 | 6 | Búsqueda rápida de clientes por nombre, número o email. Se actualiza cada segundo desde el registro de cambios (consistencia eventual). |
| [onboarding-orchestrator](services/onboarding-orchestrator.md) | 6305 | Clientes | 2 | 7 | Saga de alta F1 en 8 pasos con compensación; queda AWAITING_SIGNATURE hasta que el cliente firma el contrato marco (COMPLETED). |

## G-advice

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [notification-service](services/notification-service.md) | 6600 | Comunicación con clientes | 2 | 10 | Notificaciones por email, SMS y push con preferencias, consentimiento, horas de silencio y buzón de pruebas efímero (24 h). |
| [message-template-service](services/message-template-service.md) | 6601 | Comunicación con clientes | 2 | 6 | Plantillas de email, SMS y push en sv, en y es, versionadas. Renderiza {{variables}}; si falta alguna, 422 con la lista. Idioma desconocido → en. |

## H-contracts

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [contract-service](services/contract-service.md) | 6700 | Contratos y documentos | 2 | 22 | Contratos con plantilla versionada, cláusulas condicionales, anexos, firma múltiple con NordID, enmiendas, desistimiento, PDF de un solo uso y verificación de integridad. |
| [contract-template-service](services/contract-template-service.md) | 6701 | Contratos y documentos | 2 | 7 | Plantillas versionadas: contrato marco (v1-v3), mandato de asesoramiento (v1-v2), gestión discrecional y consentimiento de grabación. |
| [clause-engine](services/clause-engine.md) | 6702 | Contratos y documentos | 2 | 5 | Evalúa las condiciones de las cláusulas con los datos del cliente y devuelve las incluidas y excluidas con su motivo. |
| [signing-service](services/signing-service.md) | 6703 | Contratos y documentos | 2 | 6 | Sesiones de firma por parte: NordID firma el contentHash; al completarse se registra en contract-service (que comprueba hash y versión de plantilla). |
| [document-vault](services/document-vault.md) | 6704 | Contratos y documentos | 2 | 11 | Documentos cifrados con caducidad y autodestrucción (crypto-shredding), contentHash, y enlaces de descarga de un solo uso. |
| [render-service](services/render-service.md) | 6705 | Contratos y documentos | 2 | 4 | Genera PDF reales (A4, varias páginas) a partir de un título y líneas de texto. format=json devuelve base64 + SHA-256. |
| [antivirus-scan](services/antivirus-scan.md) | 6706 | Contratos y documentos | 2 | 5 | Escaneo de adjuntos: EICAR = INFECTED, tipo real por magic bytes frente al declarado, límite de 5 MB. Acepta multipart o el fichero en el cuerpo. |

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
| [portfolio-service](services/portfolio-service.md) | 6900 | Inversiones | 1 | 10 | Carteras (ISK, KF, DEPOT) con asignación objetivo por fondo. Valida fondos con product-catalog y permisos con permission-service. |
| [allocation-engine](services/allocation-engine.md) | 6901 | Inversiones | 1 | 4 | Reglas de asignación: porcentaje en renta variable según riesgo, tope por horizonte corto y sustitución ESG. Siempre suma 100. |
| [order-management](services/order-management.md) | 6902 | Inversiones | 2 | 12 | Órdenes de fondos con Idempotency-Key, hora de corte, retención de efectivo, control de participaciones y ciclo de vida completo. |
| [order-router](services/order-router.md) | 6903 | Inversiones | 2 | 6 | Ventana de lotes: a la hora de corte agrupa las órdenes por fecha de negociación y las envía al custodio. Reintenta si el custodio no está. |
| [execution-service](services/execution-service.md) | 6904 | Inversiones | 2 | 7 | Ejecuciones y liquidaciones del custodio: las enlaza con la orden, las contabiliza y publica trade.executed o trade.rejected. |
| [position-keeping](services/position-keeping.md) | 6905 | Inversiones | 2 | 11 | Participaciones por cartera y fondo a partir de movimientos (compras, ventas, desdoblamientos, fusiones, cierres), con coste medio y plusvalía realizada. |
| [valuation-service](services/valuation-service.md) | 6906 | Inversiones | 2 | 5 | Valoración de carteras a una fecha: participaciones × NAV × tipo de cambio, con coste, plusvalía latente y peso de cada fondo. |
| [performance-service](services/performance-service.md) | 6907 | Inversiones | 2 | 4 | Rentabilidad ponderada por tiempo (TWR) y por dinero (MWR/TIR anualizada) entre dos fechas, con la serie diaria de valores y flujos. |
| [rebalancing-engine](services/rebalancing-engine.md) | 6908 | Inversiones | 2 | 9 | Desviación frente a la asignación objetivo y saga de rebalanceo (ventas primero, compras con lo cobrado). Umbral en config (driftThresholdPp). |
| [corporate-actions](services/corporate-actions.md) | 6909 | Inversiones | 2 | 8 | Dividendos, desdoblamientos, fusiones y cierres de fondos: anuncio, aplicación en la fecha efectiva, efectivo al ledger y participaciones a position-keeping. |

## K-payments

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [payments-gateway](services/payments-gateway.md) | 7000 | Pagos y contabilidad | 2 | 11 | Ingresos con referencia OCR y retiradas con Idempotency-Key, step-up (acr=high) por encima de 10 000 SEK, límite diario y saga asíncrona con el banco. |
| [payment-instructions](services/payment-instructions.md) | 7001 | Pagos y contabilidad | 2 | 8 | Instrucciones de pago hacia NordPay: identificadores ISO 20022, cola de reintentos si el banco cae y estados SENT → SETTLED / REJECTED. |
| [bank-account-verification](services/bank-account-verification.md) | 7002 | Pagos y contabilidad | 2 | 8 | Cuentas bancarias de destino del cliente: IBAN válido, titularidad confirmada por el banco, revisión manual si hay dudas y step-up para añadirlas. |
| [ledger-service](services/ledger-service.md) | 7003 | Pagos y contabilidad | 2 | 14 | Contabilidad de doble entrada: asientos cuadrados, inmutables, con reversión, saldos por cuenta y balance de comprobación. |
| [cash-management](services/cash-management.md) | 7004 | Pagos y contabilidad | 2 | 10 | Saldo contable (ledger), retenciones y saldo disponible del efectivo de cada cliente. Las retenciones son idempotentes por referencia. |
| [reconciliation-service](services/reconciliation-service.md) | 7005 | Pagos y contabilidad | 2 | 7 | Cuadre diario: ledger contra extracto del banco y libro de posiciones contra el custodio. Lista los descuadres (breaks). |
| [fee-billing](services/fee-billing.md) | 7006 | Pagos y contabilidad | 2 | 7 | Comisión de gestión mensual: valoración a fin de mes × comisión anual / 12, cargo en el ledger, una vez por mes. |
| [refunds-returns](services/refunds-returns.md) | 7007 | Pagos y contabilidad | 2 | 9 | Pagos devueltos por el banco (AC04, AC06, AM02...) y devoluciones de cargos mediante asientos de reversión. |
| [gl-export](services/gl-export.md) | 7008 | Pagos y contabilidad | 2 | 7 | Exporta los asientos de un día a CSV con totales y checksum SHA-256. Una sola exportación por día salvo force=true (versión nueva). |

## O-operations

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [event-bus](services/event-bus.md) | 7400 | Plataforma de eventos | 2 | 13 | Bus de eventos: temas, lectura por cursor, suscripciones por webhook con reintentos y cola de mensajes muertos. Sin RabbitMQ hasta el nivel 12. |
| [outbox-relay](services/outbox-relay.md) | 7401 | Plataforma de eventos | 2 | 7 | Publica en event-bus los eventos guardados en el outbox de cada servicio. Pausable para practicar consistencia eventual. |
| [audit-service](services/audit-service.md) | 7402 | Cumplimiento técnico | 2 | 13 | Registro de auditoría inmutable (cadena SHA-256). Recibe registros directos y copia cada evento del bus. No admite PUT ni DELETE. |

## P-integrations

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [custody-adapter](services/custody-adapter.md) | 7500 | Integraciones | 2 | 9 | Adaptador del custodio E4: envía lotes, recibe webhooks firmados (HMAC), deduplica y publica ejecuciones y rechazos como eventos. |
| [price-feed-adapter](services/price-feed-adapter.md) | 7501 | Integraciones | 2 | 7 | Adaptador del proveedor de precios E5 con caché por ETag, contadores de 304 y respuesta stale si el proveedor cae. |
| [nordpay-adapter](services/nordpay-adapter.md) | 7502 | Integraciones | 2 | 10 | Adaptador del banco E6: transferencias ISO 20022 firmadas (JWS), estados por webhook, avisos de abono, titularidad de cuentas y extracto. |

## X-external

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [nordid-sim](services/nordid-sim.md) | 7701 | Sistema externo (simulado) | 2 | 12 | E1 NordID: autenticación y firma electrónica con orderRef y sondeo de collect. Escenarios por personalNumber en /__admin/scenarios. |
| [custodian-sim](services/custodian-sim.md) | 7704 | Sistema externo (simulado) | 2 | 12 | E4 Custodio: lotes de órdenes, ejecución al NAV del día (con comisiones del folleto y cambio de divisa), webhooks firmados y extracto de posiciones. |
| [pricefeed-sim](services/pricefeed-sim.md) | 7705 | Sistema externo (simulado) | 2 | 8 | E5 Proveedor de precios: NAV por fondo y fecha, descarga masiva por día, ETag/304 y API key. |
| [nordpay-sim](services/nordpay-sim.md) | 7706 | Sistema externo (simulado) | 2 | 14 | E6 NordPay: transferencias firmadas con JWS, estados ACCP/ACSC/RJCT por webhook, verificación de titularidad, abonos entrantes y extracto camt.053. |

**79 servicios · 616 endpoints documentados.**
