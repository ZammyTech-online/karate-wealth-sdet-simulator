# Portal de desarrolladores de Nordvik

Generado automáticamente a partir de los servicios en marcha (perfil **completo**). No lo edites a mano: ejecuta `node tools/docs-gen.mjs`.

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
| [jwt-forge](services/jwt-forge.md) | 6014 | Plataforma de datos de prueba | 3 | 6 | Tokens de prueba firmados con claims a medida: caducados, sin scope, de otro emisor, con firma rota o payload manipulado. Y un decodificador. |

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
| [composition-service](services/composition-service.md) | 6107 | Plataforma | 2 | 13 | Composición de APIs: contrato + anexo (C1), posiciones con fondo (C2), extracto mensual (C3), ficha 360 (C4), contrato marco (C5), mandato (C6), plan de pensión (C7), traspaso (C8), llamada (C9) y portabilidad (C10). |
| [correlation-tracer](services/correlation-tracer.md) | 6108 | Plataforma | 2 | 4 | Reconstruye la cadena de llamadas de un correlationId a partir de los logs (servicio, ruta, estado, duración y anidamiento). |
| [secrets-service](services/secrets-service.md) | 6109 | Plataforma | 3 | 9 | Secretos con versiones (claves de webhooks, API keys de los simuladores), rotación con periodo de gracia y verificación sin revelar el valor. |
| [service-registry](services/service-registry.md) | 6110 | Plataforma | 4 | 8 | Registro de servicios: puerto, versión, dominio y oleada de cada uno, dependencias directas y transitivas, dependientes y sondeo de salud. |
| [schema-registry](services/schema-registry.md) | 6111 | Plataforma | 4 | 7 | Versiones de esquemas de eventos por topic con compatibilidad BACKWARD/FORWARD/NONE, validación de payloads y razones de incompatibilidad. |
| [feature-flags](services/feature-flags.md) | 6112 | Plataforma | 4 | 8 | Feature flags con lista de permitidos y bloqueados y despliegue por porcentaje determinista por cliente; versión e historial de cambios. |

## C-identity

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [iam-service](services/iam-service.md) | 6200 | Identidad | 1 | 8 | Login OAuth2 (password, client_credentials, refresh_token), tokens JWT de 5 minutos, introspección, revocación y bloqueo tras 5 intentos fallidos. |
| [session-service](services/session-service.md) | 6201 | Identidad | 1 | 8 | Sesiones activas por usuario: alta al iniciar sesión, caducidad por inactividad y cierre remoto. |
| [permission-service](services/permission-service.md) | 6202 | Identidad | 1 | 8 | Decide permisos por rol y propiedad del recurso: el cliente ve lo suyo, el asesor a sus clientes asignados, back office según su función. |
| [eid-broker](services/eid-broker.md) | 6203 | Identidad | 2 | 7 | Autenticación y firma con NordID (E1): orderRef, sondeo, caducidad a 3 min y borrado del resultado a los 10 min (dato efímero). |
| [mfa-service](services/mfa-service.md) | 6204 | Identidad | 2 | 6 | Segundo factor: código de 6 dígitos por SMS o email (vía notification-service), 5 min, 3 intentos. Solo se guarda el hash del código. |
| [step-up-service](services/step-up-service.md) | 6205 | Identidad | 2 | 6 | Re-autenticación (acr=high) con NordID o MFA. El token resultante dura 5 minutos y lo exigen payments-gateway y bank-account-verification. |
| [token-exchange](services/token-exchange.md) | 6206 | Identidad | 3 | 5 | Intercambio de tokens (RFC 8693): token del usuario → token de menos permisos para un servicio concreto, más corto y sin poder ampliar scopes. |
| [delegation-service](services/delegation-service.md) | 6207 | Identidad | 4 | 10 | Apoderados y tutelas: permisos por ámbito (READ, TRADE, PAYMENTS) con fechas, verificación a cuatro ojos, revocación y comprobación de si alguien puede actuar por otro. |
| [device-trust](services/device-trust.md) | 6208 | Identidad | 4 | 9 | Dispositivos de confianza: huella del dispositivo, confianza solo con step-up (acr=high), máximo 3 por cliente, revocación y evaluación de riesgo. |

## D-customer

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [customer-registry](services/customer-registry.md) | 6300 | Clientes | 1 | 12 | Alta, consulta, modificación (con If-Match) y cierre de clientes. Enmascara el número nacional según el rol y publica un registro de cambios. |
| [contact-service](services/contact-service.md) | 6301 | Clientes | 1 | 8 | Medios de contacto del cliente con verificación por código: 3 intentos, caducidad de 10 minutos y bloqueo. |
| [customer-address-service](services/customer-address-service.md) | 6302 | Clientes | 1 | 7 | Domicilio actual e historial de domicilios del cliente; el código postal debe corresponder a la ciudad. |
| [preferences-service](services/preferences-service.md) | 6303 | Clientes | 1 | 6 | Preferencias de comunicación del cliente. Si nunca las guardó, devuelve los valores por defecto (isDefault=true). |
| [customer-search](services/customer-search.md) | 6304 | Clientes | 1 | 6 | Búsqueda rápida de clientes por nombre, número o email. Se actualiza cada segundo desde el registro de cambios (consistencia eventual). |
| [onboarding-orchestrator](services/onboarding-orchestrator.md) | 6305 | Clientes | 2 | 9 | Saga de alta F1 en 8 pasos con compensación; queda AWAITING_SIGNATURE hasta que el cliente firma el contrato marco (COMPLETED). |
| [household-service](services/household-service.md) | 6306 | Cliente | 3 | 6 | Unidad familiar: cónyuge o pareja, hijos y dependientes, con reglas de coherencia (una sola pareja, hijos nacidos después que el cliente) y números nacionales tokenizados. |
| [employment-service](services/employment-service.md) | 6307 | Cliente | 3 | 7 | Empleador actual e historial laboral: sin solapes, número de organización con Luhn, convenio colectivo y sueldo visible solo para quien debe (al asesor, el tramo). |
| [tax-residency-service](services/tax-residency-service.md) | 6308 | Cliente | 3 | 8 | Residencias fiscales y números fiscales (TIN) con validación por país, una principal obligatoria, y banderas FATCA y CRS. |
| [segmentation-service](services/segmentation-service.md) | 6309 | Cliente | 4 | 6 | Segmento del cliente por reglas versionadas (patrimonio valorado y edad): PRIVATE_BANKING, PREMIUM, YOUNG_SAVER o RETAIL, con lo que falta para el siguiente. |

## E-compliance

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [kyc-aml-service](services/kyc-aml-service.md) | 6400 | Cumplimiento normativo | 3 | 9 | Casos KYC: cribado + PEP + riesgo en orden, aprobación automática según riesgo, revisión de Cumplimiento con cuatro ojos y fecha de próxima revisión. |
| [sanctions-adapter](services/sanctions-adapter.md) | 6401 | Cumplimiento normativo | 3 | 8 | Cribado de sanciones y PEP contra SanctionScreen (E3): umbral configurable, respuestas parciales = INCOMPLETE, decisión de Cumplimiento sobre posibles coincidencias. |
| [pep-service](services/pep-service.md) | 6402 | Cumplimiento normativo | 3 | 7 | Estado PEP/RCA del cliente: autodeclaración + cribado de la lista PEP, con detección de declaraciones que no cuadran. |
| [risk-scoring](services/risk-scoring.md) | 6403 | Cumplimiento normativo | 3 | 7 | Riesgo de blanqueo 0-100 por reglas explicables (PEP, sanciones, residencia fiscal, alertas, edad); LOW, MEDIUM, HIGH o PROHIBITED. |
| [transaction-monitoring](services/transaction-monitoring.md) | 6404 | Cumplimiento normativo | 3 | 13 | Alertas por patrones: estructuración, transferencias grandes, entrada y salida rápida, países de riesgo. Ventanas sobre el reloj virtual. |
| [suitability-service](services/suitability-service.md) | 6405 | Cumplimiento normativo | 3 | 8 | Test de idoneidad MiFID II: cuestionario → perfil de riesgo 1-7, y comprobación fondo a fondo (SRI, horizonte, sostenibilidad). Caduca a los 2 años. |
| [appropriateness-service](services/appropriateness-service.md) | 6406 | Cumplimiento normativo | 3 | 8 | Test de conveniencia para productos complejos (SRI 6-7) sin asesoramiento: NOT_REQUIRED, APPROPRIATE o NOT_APPROPRIATE con advertencia y reconocimiento. |
| [complaints-service](services/complaints-service.md) | 6407 | Cumplimiento | 4 | 9 | Reclamaciones de clientes con plazos legales en días hábiles (acuse en 2, respuesta en 15), caso en case-management, resolución y vencidas. |
| [regulatory-reporting](services/regulatory-reporting.md) | 6408 | Cumplimiento | 4 | 7 | Informes al supervisor: operaciones ejecutadas por día (T+1, seudonimizadas, agregadas por ISIN) e incidentes de datos por mes; marca los envíos tardíos. |

## F-privacy

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [pii-vault](services/pii-vault.md) | 6500 | Privacidad y protección de datos | 3 | 13 | Tokenización de datos personales: cifrados con la clave del cliente, detokenización con propósito y rastro, y borrado por crypto-shredding. |
| [key-management](services/key-management.md) | 6501 | Privacidad y protección de datos | 3 | 14 | Claves AES-256 por sujeto con versiones, rotación, cifrado/descifrado y crypto-shredding (triturar la clave hace ilegible el dato en todas partes). |
| [consent-ledger](services/consent-ledger.md) | 6502 | Privacidad y protección de datos | 3 | 11 | Consentimientos por propósito en un libro encadenado por hash: estado actual, historial completo y verificación de integridad. Denegado por defecto. |
| [data-passport](services/data-passport.md) | 6503 | Privacidad y protección de datos | 3 | 9 | Pasaportes del dato: lectura de un dato personal limitada por propósito, destinatario, número de lecturas y caducidad (reloj virtual). |
| [lineage-service](services/lineage-service.md) | 6504 | Privacidad y protección de datos | 3 | 9 | Linaje de datos personales: cada tokenización, lectura y pasaporte queda como un salto con servicio, propósito, actor y correlationId. |
| [redaction-service](services/redaction-service.md) | 6505 | Privacidad y protección de datos | 3 | 6 | Redacta PII de texto libre: número nacional, IBAN, email, teléfono y tarjeta, solo si validan. Modos MASK, REMOVE y TOKENIZE (vía pii-vault). |
| [retention-scheduler](services/retention-scheduler.md) | 6506 | Privacidad y protección de datos | 3 | 12 | Políticas de retención por clase de dato, registro de datos con vencimiento, ejecuciones de purga con el reloj virtual y retención legal (legal hold). |
| [erasure-orchestrator](services/erasure-orchestrator.md) | 6507 | Privacidad | 4 | 6 | Saga del derecho al olvido: comprobación de bloqueos, crypto-shredding, retirada de consentimientos, cierre de la ficha, datos retenidos por ley y certificado. |
| [dsar-service](services/dsar-service.md) | 6508 | Privacidad | 4 | 7 | Derechos de acceso y portabilidad: paquete con los datos del cliente reunidos de varios servicios, plazo de 30 días, 3 descargas y caducidad a los 7 días. |
| [breach-service](services/breach-service.md) | 6509 | Privacidad | 4 | 11 | Brechas de datos: plazo de 72 h para avisar a la autoridad, aviso a los afectados si el riesgo es alto, marcas de retraso y cierre. |

## G-advice

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [notification-service](services/notification-service.md) | 6600 | Comunicación con clientes | 2 | 12 | Notificaciones por email, SMS y push con preferencias, consentimiento, horas de silencio y buzón de pruebas efímero (24 h). |
| [message-template-service](services/message-template-service.md) | 6601 | Comunicación con clientes | 2 | 6 | Plantillas de email, SMS y push en sv, en y es, versionadas. Renderiza {{variables}}; si falta alguna, 422 con la lista. Idioma desconocido → en. |
| [advisor-directory](services/advisor-directory.md) | 6602 | Asesoramiento y comunicación | 3 | 10 | Directorio de asesores y oficinas: especialidades, idiomas, horario, ausencias y clientes asignados (desde permission-service). |
| [advisor-desk](services/advisor-desk.md) | 6603 | Asesoramiento y comunicación | 3 | 12 | Expediente de asesoramiento: datos confirmados en llamadas (con evidencia), idoneidad y recomendaciones que solo nacen si son idóneas; aceptación del cliente con orden idempotente. |
| [meeting-scheduler](services/meeting-scheduler.md) | 6604 | Asesoramiento y comunicación | 3 | 10 | Agenda del asesor: huecos de 60 min en su horario y días hábiles, reservas sin solapes, cancelación tardía (<24 h) y recordatorios 24 h antes por notification-service. |
| [secure-messaging](services/secure-messaging.md) | 6605 | Asesoramiento y comunicación | 3 | 9 | Mensajería cifrada cliente-asesor: clave por conversación, mensajes de lectura única que se destruyen al abrirlos, caducidad y bloqueo de PII fuera de la lectura única. |
| [voice-service](services/voice-service.md) | 6606 | Asesoramiento y comunicación | 3 | 25 | Llamadas grabadas con consentimiento: turnos cifrados por llamada, cadena de eventos (transcripción, redacción, extracción, revisión, resumen) y destrucción verificable con recibo firmado. |
| [transcription-service](services/transcription-service.md) | 6607 | Asesoramiento y comunicación | 3 | 11 | Transcripción con hablantes y tiempos, redactada ([NATIONAL_ID], [IBAN], [HEALTH]...) con originales en pii-vault; el original completo se lee una sola vez y muere con la clave de la llamada. |
| [insight-extraction](services/insight-extraction.md) | 6608 | Asesoramiento y comunicación | 3 | 10 | Datos estructurados desde la transcripción redactada (aportación, horizonte, riesgo, objetivo, orden, cláusula) con evidencia y revisión humana por debajo de 0,75 de confianza. |
| [call-summary](services/call-summary.md) | 6609 | Asesoramiento y comunicación | 3 | 8 | Resumen de 5 líneas sin PII (comprobado con redaction-service) de cada llamada, conservado 10 años; su evento dispara la destrucción del resto. |

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
| [archive-service](services/archive-service.md) | 6707 | Contratos y documentos | 4 | 13 | Archivo legal inmutable de 10 años: cadena de hashes, verificación de integridad, prohibido modificar o borrar y purga solo tras la retención. |

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
| [esg-data](services/esg-data.md) | 6808 | Mercado | 4 | 7 | Datos de sostenibilidad por fondo (puntuación, rating, SFDR, carbono), ranking, ESG ponderada de una cartera y perfil ESG del cliente con consentimiento. |

## J-investment

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [portfolio-service](services/portfolio-service.md) | 6900 | Inversiones | 1 | 10 | Carteras (ISK, KF, DEPOT) con asignación objetivo por fondo. Valida fondos con product-catalog y permisos con permission-service. |
| [allocation-engine](services/allocation-engine.md) | 6901 | Inversiones | 1 | 4 | Reglas de asignación: porcentaje en renta variable según riesgo, tope por horizonte corto y sustitución ESG. Siempre suma 100. |
| [order-management](services/order-management.md) | 6902 | Inversiones | 2 | 14 | Órdenes de fondos con Idempotency-Key, hora de corte, retención de efectivo, control de participaciones y ciclo de vida completo. |
| [order-router](services/order-router.md) | 6903 | Inversiones | 2 | 6 | Ventana de lotes: a la hora de corte agrupa las órdenes por fecha de negociación y las envía al custodio. Reintenta si el custodio no está. |
| [execution-service](services/execution-service.md) | 6904 | Inversiones | 2 | 9 | Ejecuciones y liquidaciones del custodio: las enlaza con la orden, las contabiliza y publica trade.executed o trade.rejected. |
| [position-keeping](services/position-keeping.md) | 6905 | Inversiones | 2 | 13 | Participaciones por cartera y fondo a partir de movimientos (compras, ventas, desdoblamientos, fusiones, cierres), con coste medio y plusvalía realizada. |
| [valuation-service](services/valuation-service.md) | 6906 | Inversiones | 2 | 5 | Valoración de carteras a una fecha: participaciones × NAV × tipo de cambio, con coste, plusvalía latente y peso de cada fondo. |
| [performance-service](services/performance-service.md) | 6907 | Inversiones | 2 | 4 | Rentabilidad ponderada por tiempo (TWR) y por dinero (MWR/TIR anualizada) entre dos fechas, con la serie diaria de valores y flujos. |
| [rebalancing-engine](services/rebalancing-engine.md) | 6908 | Inversiones | 2 | 11 | Desviación frente a la asignación objetivo y saga de rebalanceo (ventas primero, compras con lo cobrado). Umbral en config (driftThresholdPp). |
| [corporate-actions](services/corporate-actions.md) | 6909 | Inversiones | 2 | 8 | Dividendos, desdoblamientos, fusiones y cierres de fondos: anuncio, aplicación en la fecha efectiva, efectivo al ledger y participaciones a position-keeping. |

## K-payments

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [payments-gateway](services/payments-gateway.md) | 7000 | Pagos y contabilidad | 2 | 13 | Ingresos con referencia OCR y retiradas con Idempotency-Key, step-up (acr=high) por encima de 10 000 SEK, límite diario y saga asíncrona con el banco. |
| [payment-instructions](services/payment-instructions.md) | 7001 | Pagos y contabilidad | 2 | 10 | Instrucciones de pago hacia NordPay: identificadores ISO 20022, cola de reintentos si el banco cae y estados SENT → SETTLED / REJECTED. |
| [bank-account-verification](services/bank-account-verification.md) | 7002 | Pagos y contabilidad | 2 | 8 | Cuentas bancarias de destino del cliente: IBAN válido, titularidad confirmada por el banco, revisión manual si hay dudas y step-up para añadirlas. |
| [ledger-service](services/ledger-service.md) | 7003 | Pagos y contabilidad | 2 | 14 | Contabilidad de doble entrada: asientos cuadrados, inmutables, con reversión, saldos por cuenta y balance de comprobación. |
| [cash-management](services/cash-management.md) | 7004 | Pagos y contabilidad | 2 | 10 | Saldo contable (ledger), retenciones y saldo disponible del efectivo de cada cliente. Las retenciones son idempotentes por referencia. |
| [reconciliation-service](services/reconciliation-service.md) | 7005 | Pagos y contabilidad | 2 | 7 | Cuadre diario: ledger contra extracto del banco y libro de posiciones contra el custodio. Lista los descuadres (breaks). |
| [fee-billing](services/fee-billing.md) | 7006 | Pagos y contabilidad | 2 | 7 | Comisión de gestión mensual: valoración a fin de mes × comisión anual / 12, cargo en el ledger, una vez por mes. |
| [refunds-returns](services/refunds-returns.md) | 7007 | Pagos y contabilidad | 2 | 11 | Pagos devueltos por el banco (AC04, AC06, AM02...) y devoluciones de cargos mediante asientos de reversión. |
| [gl-export](services/gl-export.md) | 7008 | Pagos y contabilidad | 2 | 7 | Exporta los asientos de un día a CSV con totales y checksum SHA-256. Una sola exportación por día salvo force=true (versión nueva). |

## L-pensions

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [pension-plan-service](services/pension-plan-service.md) | 7100 | Pensiones | 3 | 11 | Planes de pensión privados, de empresa (por convenio) y de intercambio salarial, con su cartera PENSION, elegibilidad de cobro (55 años) y congelación. |
| [contribution-service](services/contribution-service.md) | 7101 | Pensiones | 3 | 8 | Aportaciones a planes de pensión (privadas, de empresa, intercambio salarial, traspasos): asiento en ledger e inversión inmediata según la asignación, sin duplicados. |
| [employer-portal-api](services/employer-portal-api.md) | 7102 | Pensiones | 3 | 7 | API de empresas con API key (X-Employer-Key): alta de empleados con su plan OCCUPATIONAL por convenio, consulta enmascarada y aportaciones del mes. |
| [employer-file-ingest](services/employer-file-ingest.md) | 7103 | Pensiones | 3 | 8 | Ficheros mensuales de empleadores: validación línea a línea (empleado, plan, importe, convenio, mes repetido), aportación de las válidas, cola de errores y acuse. |
| [collective-agreement-rules](services/collective-agreement-rules.md) | 7104 | Pensiones | 3 | 6 | Reglas de cada convenio: edad de inicio, porcentajes por tramo (hasta 7,5 importes base y por encima), bonificación del intercambio salarial y consolidación. |
| [salary-exchange-service](services/salary-exchange-service.md) | 7105 | Pensiones | 3 | 7 | Intercambio salarial: simulación (bonificación del convenio, coste neto aproximado) y alta, con el aviso del umbral de 8,07 importes base. |
| [vesting-service](services/vesting-service.md) | 7106 | Pensiones | 3 | 4 | Derechos consolidados de un plan: según el convenio y los años en la empresa (employment-service); privados e intercambio salarial, 100 %. |
| [beneficiary-service](services/beneficiary-service.md) | 7107 | Pensiones | 3 | 7 | Beneficiarios de cada plan: porcentajes que suman 100,00, solo familia cercana, números nacionales tokenizados y cambios con If-Match. |
| [transfer-service](services/transfer-service.md) | 7108 | Pensiones | 3 | 12 | Traspasos entrantes desde otras gestoras: saga con valoración, confirmación del cliente, aprobación de Operaciones, tolerancia del 2 %, revisión manual e historial. |
| [transfer-valuation](services/transfer-valuation.md) | 7109 | Pensiones | 3 | 8 | Valoración previa de pólizas externas para traspasos: asíncrona (1–3 días virtuales), validez de 30 días y comparación con tolerancia del 2 %. |
| [payout-planner](services/payout-planner.md) | 7110 | Pensiones | 3 | 13 | Planes de cobro: elegibilidad, capital valorado, renta temporal o vitalicia (annuity-calculator) y ciclo DRAFT → ACTIVE ⇄ SUSPENDED → FINISHED. |
| [payout-execution](services/payout-execution.md) | 7111 | Pensiones | 3 | 8 | Ejecución mensual de pagos de pensión: bruto, retención según tax-withholding (30 % provisional si no responde), neto y asiento; idempotente por plan y mes. |
| [annuity-calculator](services/annuity-calculator.md) | 7112 | Pensiones | 3 | 5 | Rentas temporales y vitalicias con tabla de mortalidad sintética (Gompertz-Makeham) e interés técnico; esperanza de vida y factores, deterministas. |
| [death-claims](services/death-claims.md) | 7113 | Pensiones | 3 | 9 | Reclamaciones por fallecimiento: congelación de planes, suspensión de cobros, verificación, reparto por beneficiarios (o ESTATE) y liquidación. |

## M-insurance

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [insurance-policy](services/insurance-policy.md) | 7200 | Seguros | 4 | 11 | Pólizas de vida e invalidez ligadas al plan de pensión: declaración de salud vigente, prima con recargo, emisión en la aseguradora y cancelación. |
| [health-declaration](services/health-declaration.md) | 7201 | Seguros | 4 | 7 | Cuestionario de salud efímero con consentimiento explícito: se suscribe en la aseguradora y solo queda el resultado, que caduca a los 90 días. |
| [premium-calculator](services/premium-calculator.md) | 7202 | Seguros | 4 | 4 | Prima mensual de vida o invalidez según edad, capital, plazo y recargo médico; tabla sintética y 15 % de gastos, determinista. |
| [insurance-claims](services/insurance-claims.md) | 7203 | Seguros | 4 | 6 | Siniestros de vida e invalidez: apertura por Operaciones, envío a la aseguradora, sincronización del estado y cobro contable del capital aprobado. |

## N-tax

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [tax-withholding](services/tax-withholding.md) | 7300 | Fiscalidad | 4 | 6 | Tipo de retención por cliente: tabla municipal de la Agencia Tributaria, SINK del 25 % para no residentes y 30 % para rentas del capital. |
| [tax-reporting](services/tax-reporting.md) | 7301 | Fiscalidad | 4 | 8 | Informe fiscal anual en lote: pagos de pensión del año por cliente, fichero de control a la Agencia Tributaria con acuse; original y correcciones. |
| [annual-statement](services/annual-statement.md) | 7302 | Fiscalidad | 4 | 5 | Certificado fiscal anual del cliente en JSON y PDF a partir del informe presentado (el último, si hubo corrección). |
| [statement-service](services/statement-service.md) | 7303 | Fiscalidad | 4 | 5 | Extractos mensuales y trimestrales de una cartera: valor inicial y final, órdenes ejecutadas y variación, en JSON y PDF. |
| [analytics-reporting](services/analytics-reporting.md) | 7304 | Fiscalidad | 4 | 5 | Informes internos anonimizados con k-anonimato (k ≥ 5): planes por tipo y edad, aportaciones por origen; los grupos pequeños se suprimen. |

## O-operations

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [event-bus](services/event-bus.md) | 7400 | Plataforma de eventos | 2 | 13 | Bus de eventos: temas, lectura por cursor, suscripciones por webhook con reintentos y cola de mensajes muertos. Sin RabbitMQ hasta el nivel 12. |
| [outbox-relay](services/outbox-relay.md) | 7401 | Plataforma de eventos | 2 | 7 | Publica en event-bus los eventos guardados en el outbox de cada servicio. Pausable para practicar consistencia eventual. |
| [audit-service](services/audit-service.md) | 7402 | Cumplimiento técnico | 2 | 15 | Registro de auditoría inmutable (cadena SHA-256). Recibe registros directos y copia cada evento del bus. No admite PUT ni DELETE. |
| [case-management](services/case-management.md) | 7403 | Operaciones y back office | 3 | 14 | Casos con SLA por tipo y prioridad (reloj virtual), notas, asignación y resolución; se abren solos desde alertas AML y KYC en revisión. |
| [task-queue](services/task-queue.md) | 7404 | Operaciones y back office | 3 | 11 | Tareas humanas por rol: crear, reclamar, completar o soltar; vencimiento con el reloj virtual y quién hizo cada cosa. |
| [approval-service](services/approval-service.md) | 7405 | Operaciones y back office | 3 | 8 | Aprobaciones con cuatro ojos: quien pide no aprueba, 1 o 2 aprobadores distintos del rol, un rechazo cierra, caducidad con el reloj virtual y tarea en task-queue. |
| [dead-letter-service](services/dead-letter-service.md) | 7406 | Operaciones | 4 | 7 | Eventos que nadie pudo procesar (consumidores internos y webhooks): lista unificada, reintento manual, descarte con motivo y registro de decisiones. |

## P-integrations

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [custody-adapter](services/custody-adapter.md) | 7500 | Integraciones | 2 | 9 | Adaptador del custodio E4: envía lotes, recibe webhooks firmados (HMAC), deduplica y publica ejecuciones y rechazos como eventos. |
| [price-feed-adapter](services/price-feed-adapter.md) | 7501 | Integraciones | 2 | 7 | Adaptador del proveedor de precios E5 con caché por ETag, contadores de 304 y respuesta stale si el proveedor cae. |
| [nordpay-adapter](services/nordpay-adapter.md) | 7502 | Integraciones | 2 | 10 | Adaptador del banco E6: transferencias ISO 20022 firmadas (JWS), estados por webhook, avisos de abono, titularidad de cuentas y extracto. |
| [population-adapter](services/population-adapter.md) | 7503 | Integraciones | 3 | 6 | Consulta al Registro de Población (E2) con mTLS simulado: identidad protegida sin dirección, fallecidos (evento) y comparación con la ficha del cliente. |
| [public-pension-adapter](services/public-pension-adapter.md) | 7504 | Integraciones | 3 | 4 | Consulta a la Autoridad de Pensiones Públicas (E7): sobre firmado, verificación de la firma de la respuesta y mantenimiento con Retry-After. |
| [employer-clearing-adapter](services/employer-clearing-adapter.md) | 7505 | Integraciones | 3 | 5 | Webhooks del Clearing de Empleadores (E9) con firma HMAC sobre el cuerpo exacto, descarga del CSV, entrega a employer-file-ingest y acuse de vuelta. |
| [provider-network-adapter](services/provider-network-adapter.md) | 7506 | Integraciones | 3 | 7 | Red de gestoras (E10) con firma HMAC mutua: valoraciones asíncronas de pólizas y liberación del dinero en traspasos; rechaza respuestas mal firmadas. |
| [tax-agency-adapter](services/tax-agency-adapter.md) | 7507 | Integraciones | 4 | 7 | Agencia Tributaria (E8) con API key: tablas de retención cacheadas por persona y año y entrega de ficheros anuales con su acuse. |
| [insurer-adapter](services/insurer-adapter.md) | 7508 | Integraciones | 4 | 8 | Aseguradora (E11) con OAuth2 client credentials: token cacheado, renovación ante 401 con un solo reintento; suscripción, pólizas y siniestros. |
| [legacy-mainframe-bridge](services/legacy-mainframe-bridge.md) | 7509 | Integraciones | 4 | 9 | Puente con el mainframe: ficheros de ancho fijo de 80 columnas, lote nocturno en la ventana 02:00-04:00 y códigos de retorno numéricos por línea. |
| [legacy-pensions-soap](services/legacy-pensions-soap.md) | 7510 | Integraciones | 4 | 6 | Sistema de pensiones heredado en SOAP 1.1 (planes anteriores a 2015): sobre XML, SOAPAction, SOAP Faults con HTTP 500 y WSDL. |

## Q-quality

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [chaos-control](services/chaos-control.md) | 7600 | Calidad y test | 3 | 12 | Inyecta latencia y errores en cualquier servicio (faults con probabilidad, veces y caducidad) y gestiona el catálogo de trampas T1-T30. |
| [mock-control](services/mock-control.md) | 7601 | Calidad y test | 3 | 7 | Panel único de los sistemas externos simulados: estado, comportamiento (normal, down, error, slow, failNext) y reinicio de todos a la vez. |
| [event-spy](services/event-spy.md) | 7602 | Calidad y test | 3 | 8 | Espía de eventos para tests: buscar, marcar un punto de partida, esperar a que llegue un evento (long-poll) y detectar duplicados. |
| [contract-check](services/contract-check.md) | 7603 | Calidad y test | 4 | 5 | Compara el OpenAPI publicado de un servicio con su respuesta real: obligatorios ausentes, campos sin documentar y tipos distintos (deriva de contratos). |
| [test-insights](services/test-insights.md) | 7604 | Calidad y test | 4 | 8 | Resultados de tus ejecuciones de Karate: tasa de éxito, tests inestables (flaky) con su tasa de alternancia, los más lentos y la tendencia por suite. |

## X-external

| Servicio | Puerto | Equipo | Oleada | Endpoints | Qué hace |
| --- | --- | --- | --- | --- | --- |
| [nordid-sim](services/nordid-sim.md) | 7701 | Sistema externo (simulado) | 2 | 12 | E1 NordID: autenticación y firma electrónica con orderRef y sondeo de collect. Escenarios por personalNumber en /__admin/scenarios. |
| [population-sim](services/population-sim.md) | 7702 | Sistema externo (simulado) | 3 | 10 | E2 Registro de Población: persona por número nacional con mTLS simulado, identidad protegida, fallecidos y 404; precarga en /__admin/persons. |
| [sanctionscreen-sim](services/sanctionscreen-sim.md) | 7703 | Sistema externo (simulado) | 3 | 12 | E3 SanctionScreen: cribado de sanciones y PEP con coincidencia difusa, API key rotatoria, respuestas parciales y registro de peticiones recibidas. |
| [custodian-sim](services/custodian-sim.md) | 7704 | Sistema externo (simulado) | 2 | 12 | E4 Custodio: lotes de órdenes, ejecución al NAV del día (con comisiones del folleto y cambio de divisa), webhooks firmados y extracto de posiciones. |
| [pricefeed-sim](services/pricefeed-sim.md) | 7705 | Sistema externo (simulado) | 2 | 8 | E5 Proveedor de precios: NAV por fondo y fecha, descarga masiva por día, ETag/304 y API key. |
| [nordpay-sim](services/nordpay-sim.md) | 7706 | Sistema externo (simulado) | 2 | 14 | E6 NordPay: transferencias firmadas con JWS, estados ACCP/ACSC/RJCT por webhook, verificación de titularidad, abonos entrantes y extracto camt.053. |
| [public-pension-sim](services/public-pension-sim.md) | 7707 | Sistema externo (simulado) | 3 | 9 | E7 Autoridad de Pensiones Públicas: sobre firmado (HMAC del cuerpo canónico), edad mínima de cobro por año de nacimiento, pensión pública estimada y mantenimiento con Retry-After. |
| [tax-agency-sim](services/tax-agency-sim.md) | 7708 | Sistema externo (simulado) | 4 | 11 | E8 Agencia Tributaria: tablas de retención por número nacional (con API key) y ficheros anuales de control con acuse línea a línea. |
| [employer-clearing-sim](services/employer-clearing-sim.md) | 7709 | Sistema externo (simulado) | 3 | 10 | E9 Clearing de Empleadores: ficheros CSV mensuales de aportaciones, webhook firmado con HMAC hacia Nordvik y acuses de aceptadas y rechazadas. |
| [provider-network-sim](services/provider-network-sim.md) | 7710 | Sistema externo (simulado) | 3 | 13 | E10 Red de gestoras: valoraciones de pólizas que tardan 1–3 días virtuales, traspaso del dinero y firma HMAC mutua en las dos direcciones. |
| [insurer-sim](services/insurer-sim.md) | 7711 | Sistema externo (simulado) | 4 | 16 | E11 Aseguradora: OAuth2 client credentials con caducidad, suscripción médica sin guardar respuestas, pólizas y siniestros con decisión manual. |
| [telephony-sim](services/telephony-sim.md) | 7712 | Sistema externo (simulado) | 3 | 10 | E12 Telefonía: llamadas del proveedor y guiones de turnos ya reconocidos (V2-V9, es/sv/en) con variables {PNR} e {IBAN}; guiones propios en /__admin/scripts. |

**168 servicios · 1404 endpoints documentados.**
