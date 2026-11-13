// certificacion.js: Motor de Evaluación y Generador de Diplomas Digitales SDET Nordvik (Modern Web Baseline)
'use strict';

const Certificacion = {
  currentLevel: 'junior',
  activeExam: null,
  timerInterval: null,
  timeLeftSeconds: 600, // 10 minutos
  score: 0,
  lastPercentage: 100,
  currentCertId: '',

  examQuestions: {
    junior: [
      {
        q: '¿Por qué en una API bancaria una consulta de saldo o fondos (GET) nunca debe alterar el estado del sistema?',
        opts: [
          'Porque el estándar HTTP define GET como seguro e idempotente; si mutara datos, prefetchs o reintentos debitarían fondos por error.',
          'Porque las consultas GET no admiten respuestas en formato JSON.',
          'Porque Karate DSL arroja una excepción si un método GET tarda más de 200 milisegundos.',
          'Porque los métodos GET no pueden ejecutarse a través de un API Gateway.'
        ],
        answer: 0,
        explanation: 'GET debe ser de solo lectura. Cualquier mutación financiera debe realizarse mediante POST, PUT o DELETE con autorización expresa.'
      },
      {
        q: 'Al validar una cartera de fondos con Karate DSL, ¿por qué es una mala práctica hacer "match response == string_fijo"?',
        opts: [
          'Porque Karate DSL no soporta comparaciones de cadenas.',
          'Porque el valor liquidativo (NAV) y las participaciones fluctúan en tiempo real, haciendo que los tests fallen falsamente.',
          'Porque los microservicios bancarios siempre ordenan las propiedades del JSON de forma aleatoria.',
          'Porque el operador == solo funciona con números enteros.'
        ],
        answer: 1,
        explanation: 'En finanzas, los precios y saldos varían constantemente. Debe validarse la estructura con marcadores fuzzy (#number) o usar el operador "contains".'
      },
      {
        q: '¿Qué marcador fuzzy de Karate DSL valida que un Personnummer sueco cumple con el formato regulatorio (12 dígitos)?',
        opts: [
          '#string',
          '#regex ^(19|20)\\d{6}-\\d{4}$',
          '#number',
          '#present'
        ],
        answer: 1,
        explanation: '#regex permite validar expresiones regulares exactas para documentos de identidad nacionales sin quemar identificadores personales reales.'
      },
      {
        q: '¿En qué fase del patrón AAA (Arrange-Act-Assert) de Gherkin debe situarse la validación "Then status 200"?',
        opts: [
          'En el Given, antes de enviar la petición para preparar la memoria.',
          'En el When, junto a la llamada al método HTTP.',
          'En el Then, ya que corresponde a la aserción (Assert) posterior a la acción.',
          'En el Background de forma obligatoria.'
        ],
        answer: 2,
        explanation: 'El patrón AAA exige que las aserciones ocurran en el bloque "Then", una vez que la respuesta HTTP (response) ya ha sido recibida.'
      },
      {
        q: '¿Qué ventaja tiene la directiva "param assetClass = \'FixedIncome\'" sobre concatenar "?assetClass=FixedIncome" en la URL?',
        opts: [
          'Permite a Karate codificar automáticamente caracteres especiales y mantener el test limpio y modular.',
          'Hace que la petición viaje como POST automáticamente.',
          'Evita que el servidor aplique límites de tasa (Rate Limiting).',
          'Es la única forma en que Karate puede recibir respuestas HTTP 200.'
        ],
        answer: 0,
        explanation: 'La directiva param gestiona la codificación URL (URL-encoding) y desacopla la ruta base de los filtros de consulta.'
      }
    ],

    mid: [
      {
        q: '¿Cuál es la función principal de "Scenario Outline" con tabla "Examples" en pruebas actuariales?',
        opts: [
          'Ejecutar el mismo escenario con múltiples combinaciones de datos (ej: diferentes divisas, montos y límites de validación).',
          'Evitar que el microservicio devuelva códigos de error 400.',
          'Sustituir la necesidad de un servidor API Gateway.',
          'Acelerar la velocidad de compilación en Java.'
        ],
        answer: 0,
        explanation: 'Scenario Outline permite Data-Driven Testing para comprobar matrices de validación complejas sin duplicar código de pruebas.'
      },
      {
        q: '¿Por qué se recomienda la función read(\'classpath:data/pension.json\') para payloads de pensiones ITP1?',
        opts: [
          'Porque evita ensuciar el escenario de prueba con JSONs de más de 40 campos y permite reutilizar fixtures.',
          'Porque Karate no permite escribir JSON dentro de los archivos .feature.',
          'Porque encripta automáticamente los datos del empleado.',
          'Porque read() ejecuta una llamada HTTP GET externa al disco.'
        ],
        answer: 0,
        explanation: 'read() desacopla los datos de prueba masivos de la lógica del escenario Gherkin.'
      },
      {
        q: 'Al usar karate.call(\'auth.feature\') para autenticación, ¿qué buena práctica previene sobrecargar el servidor IAM?',
        opts: [
          'Invocación única por sesión o suite usando karate.callSingle() en karate-config.js.',
          'Hacer login con un usuario diferente en cada petición.',
          'Guardar las contraseñas en texto plano dentro del repositorio Git.',
          'Desactivar la autenticación en el microservicio en producción.'
        ],
        answer: 0,
        explanation: 'karate.callSingle() ejecuta la autenticación una sola vez y cachea el token para toda la suite de pruebas.'
      },
      {
        q: 'En el cálculo de divisas con GraalVM JavaScript, ¿qué hace la función "karate.filter(response.rates, function(x){ return x.pair == \'EURSEK\' })"?',
        opts: [
          'Filtra el array de tasas devuelto por el microservicio para obtener únicamente el objeto del par euro/corona.',
          'Convierte automáticamente todas las coronas suecas a euros.',
          'Elimina la tasa EURSEK de la base de datos de Nordvik.',
          'Ordena las tasas de forma descendente por valor liquidativo.'
        ],
        answer: 0,
        explanation: 'karate.filter() ejecuta una función de predicado en memoria sobre el array JSON devuelto por la API.'
      },
      {
        q: 'Si un servicio responde con un decimal flotante (11.452), ¿qué tipo de comparación es segura en Karate?',
        opts: [
          'match response.rate == 11.452 o match response.rate == \'#number\'',
          'match response.rate == "11.452" (como string)',
          'match response.rate == true',
          'match response.rate == null'
        ],
        answer: 0,
        explanation: 'Deben compararse tipos compatibles; comparar números contra strings causa fallos de aserción en GraalVM.'
      }
    ],

    senior: [
      {
        q: '¿Por qué el uso de "java.lang.Thread.sleep()" es considerado un anti-patrón en pruebas de liquidación asíncrona?',
        opts: [
          'Porque introduce lentitud innecesaria si la orden se liquida antes, y fragilidad si la red sufre una demora temporal.',
          'Porque Karate DSL no tiene soporte para Java.',
          'Porque bloquea el puerto TCP del servidor remoto.',
          'Porque los navegadores web modernos no reconocen el método sleep.'
        ],
        answer: 0,
        explanation: 'El polling inteligente no bloqueante con "retry until" comprueba activamente el estado y avanza de inmediato cuando se cumple la condición.'
      },
      {
        q: 'En una saga distribuida de traspaso de pensiones, ¿qué debe certificar un Senior SDET si el paso de destino falla?',
        opts: [
          'Que el orquestador ejecute las transacciones de compensación reembolsando los fondos a la cuenta de origen.',
          'Que el microservicio continúe la transacción ignorando el error.',
          'Que la base de datos se borre por completo.',
          'Que se envíe una notificación SMS al cliente sin actualizar el saldo.'
        ],
        answer: 0,
        explanation: 'Una saga distribuida garantiza consistencia eventual ejecutando pasos de compensación (reembolso) ante fallos intermedios.'
      },
      {
        q: '¿Cómo evita la cabecera "Idempotency-Key" el doble cobro cuando un cliente pulsa dos veces el botón de pago?',
        opts: [
          'El servidor asocia la clave única al primer cobro procesado; peticiones idénticas posteriores devuelven el resultado previo sin debitar.',
          'Bloquea la tarjeta de crédito del usuario inmediatamente.',
          'Cambia el método POST a GET de forma transparente.',
          'Obliga al cliente a rellenar un captcha visual.'
        ],
        answer: 0,
        explanation: 'El almacén de idempotencia intercepta la segunda llamada con la misma clave y devuelve la respuesta cacheada sin re-ejecutar el cargo.'
      },
      {
        q: '¿Qué estándar define que las APIs REST deben devolver respuestas de error con { type, title, status, detail, instance }?',
        opts: [
          'RFC 9457 (Problem Details for HTTP APIs)',
          'RFC 2616 (HTTP/1.1 Obsoleto)',
          'ISO 27001',
          'PCI-DSS v4.0'
        ],
        answer: 0,
        explanation: 'El estándar RFC 9457 (sucesor del RFC 7807) estandariza los objetos JSON de error legibles por máquina y humanos.'
      },
      {
        q: '¿Qué prueba previene la vulnerabilidad IDOR (Insecure Direct Object Reference) en un extracto de cuenta?',
        opts: [
          'Verificar que un usuario autenticado A reciba HTTP 403 Forbidden al intentar acceder a los recursos del usuario B.',
          'Comprobar que todas las contraseñas tengan más de 8 caracteres.',
          'Hacer pruebas de carga con 10.000 usuarios concurrentes.',
          'Validar que la página web no tenga scroll horizontal.'
        ],
        answer: 0,
        explanation: 'Las pruebas IDOR aseguran que la capa de autorización verifique que el titular del token sea el dueño legítimo del recurso solicitado.'
      }
    ],

    staff: [
      {
        q: '¿Cuál es la principal ventaja de levantar un mock en memoria con "karate.start({ mock: ... })" en un entorno CI/CD?',
        opts: [
          'Aislar las pruebas de dependencias externas inestables (SWIFT, bolsas, pasarelas bancarias) con latencia cero y puerto dinámico.',
          'Eliminar la necesidad de compilar el código de la aplicación.',
          'Hacer que las pruebas corran en memoria sin consumir RAM.',
          'Evitar escribir aserciones en los archivos feature.'
        ],
        answer: 0,
        explanation: 'Los mocks en memoria permiten simular respuestas exactas de sistemas externos de terceros sin requerir conectividad de red.'
      },
      {
        q: 'Al inyectar latencia en un proveedor de precios de mercado, ¿qué comportamiento de resiliencia debe verificarse?',
        opts: [
          'Que el Circuit Breaker se abra ante el timeout y el Gateway devuelva el precio del último cierre sin provocar caída del clúster.',
          'Que el microservicio se reinicie continuamente hasta colapsar.',
          'Que los clientes reciban un error HTTP 500 sin explicación.',
          'Que la base de datos reintente infinitamente hasta agotar el pool de conexiones.'
        ],
        answer: 0,
        explanation: 'El patrón Circuit Breaker protege la plataforma bancaria degradando el servicio con un fallback controlado antes de que la lentitud propague fallos.'
      },
      {
        q: 'En una matriz de permisos RBAC para seguros de vida, ¿por qué es crítico probar tanto los casos positivos como los negativos?',
        opts: [
          'Para certificar que un rol sin privilegios (ej: JUNIOR_ADVISOR) reciba 403 al intentar mutar beneficiarios de pólizas.',
          'Porque los tests negativos consumen menos tiempo de CPU.',
          'Porque la normativa bancaria solo audita los casos de éxito.',
          'Para garantizar que todos los usuarios tengan el rol de Administrador.'
        ],
        answer: 0,
        explanation: 'Un fallo de seguridad ocurre cuando un usuario no autorizado ejecuta una acción prohibida. Los casos negativos certifican el bloqueo explícito.'
      },
      {
        q: '¿Por qué un Staff SDET utiliza un Reloj Virtual (Clock Service) en lugar del reloj real de la máquina para pruebas de fin de año?',
        opts: [
          'Para adelantar el tiempo a fechas clave (ej: 31 de diciembre) y verificar devengos fiscales trimestrales sin esperar meses reales.',
          'Porque los servidores en la nube no tienen reloj interno.',
          'Para sincronizar los tests con la hora del meridiano de Greenwich.',
          'Porque las fechas futuras consumen menos almacenamiento en disco.'
        ],
        answer: 0,
        explanation: 'La manipulación del reloj virtual permite probar el cierre contable, comisiones anuales y liquidación fiscal de forma determinista e instantánea.'
      },
      {
        q: '¿Qué paso es OBLIGATORIO al finalizar una prueba de inyección de caos en Karate DSL?',
        opts: [
          'Desactivar el caos (enabled: false) para restaurar el estado original del clúster y evitar contaminar tests posteriores.',
          'Apagar y reiniciar todos los servidores de la empresa.',
          'Borrar los logs de ejecución del contenedor Docker.',
          'Enviar un correo electrónico al equipo de operaciones.'
        ],
        answer: 0,
        explanation: 'El principio de higiene en pruebas exige que todo fallo inducido sea revertido para que los siguientes escenarios corran en un entorno limpio.'
      }
    ]
  },

  init() {
    this.bindEvents();
  },

  bindEvents() {
    const startBtn = document.getElementById('cert-start-btn');
    if (startBtn) {
      startBtn.onclick = () => this.startExam(BrunoGym.currentLevel || 'junior');
    }

    const submitBtn = document.getElementById('cert-submit-btn');
    if (submitBtn) {
      submitBtn.onclick = () => this.submitExam();
    }

    const levelSelect = document.getElementById('cert-level-select');
    if (levelSelect) {
      levelSelect.onchange = (e) => {
        this.currentLevel = e.target.value;
      };
    }
  },

  startExam(level = 'junior') {
    this.currentLevel = level;
    this.activeExam = this.examQuestions[level] || this.examQuestions.junior;
    this.timeLeftSeconds = 600; // 10 minutos
    this.score = 0;

    const modal = document.getElementById('cert-exam-modal');
    const container = document.getElementById('cert-questions-container');
    const title = document.getElementById('cert-exam-title');
    const levelBadge = document.getElementById('cert-exam-level');

    if (title) title.textContent = `Examen Oficial de Certificación SDET: Nivel ${this.getLevelName(level)}`;
    if (levelBadge) levelBadge.textContent = this.getLevelName(level);
    const feedbackEl = document.getElementById('cert-exam-feedback');
    if (feedbackEl) {
      feedbackEl.hidden = true;
      feedbackEl.innerHTML = '';
    }

    if (container) {
      container.innerHTML = this.activeExam.map((item, idx) => `
        <fieldset class="panel" style="margin-bottom:14px; padding:14px; border:1px solid var(--line); border-radius:8px;">
          <legend style="font-weight:700; color:var(--accent); font-size:0.92rem; padding:0 6px;">
            Pregunta ${idx + 1} de ${this.activeExam.length}
          </legend>
          <p style="margin:4px 0 10px 0; font-size:0.9rem; line-height:1.45;">${this.escape(item.q)}</p>
          <div style="display:flex; flex-direction:column; gap:8px;">
            ${item.opts.map((opt, optIdx) => `
              <label style="display:flex; align-items:flex-start; gap:8px; cursor:pointer; font-size:0.86rem; line-height:1.35; padding:6px 10px; border-radius:6px; background:var(--surface-2); border:1px solid transparent;">
                <input type="radio" name="cert_q_${idx}" value="${optIdx}" style="margin-top:2px;">
                <span>${this.escape(opt)}</span>
              </label>
            `).join('')}
          </div>
        </fieldset>
      `).join('');
    }

    this.startTimer();
    if (modal) modal.showModal();
  },

  startTimer() {
    if (this.timerInterval) clearInterval(this.timerInterval);
    const timerLabel = document.getElementById('cert-timer-label');

    const updateTimer = () => {
      const min = Math.floor(this.timeLeftSeconds / 60);
      const sec = this.timeLeftSeconds % 60;
      if (timerLabel) {
        timerLabel.textContent = `${String(min).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
        if (this.timeLeftSeconds <= 60) {
          timerLabel.style.color = 'var(--bad)';
        } else {
          timerLabel.style.color = 'var(--accent)';
        }
      }

      if (this.timeLeftSeconds <= 0) {
        clearInterval(this.timerInterval);
        this.submitExam(true);
      }
      this.timeLeftSeconds--;
    };

    updateTimer();
    this.timerInterval = setInterval(updateTimer, 1000);
  },

  submitExam(timedOut = false) {
    if (this.timerInterval) clearInterval(this.timerInterval);
    if (!this.activeExam) return;

    let correctCount = 0;
    const missed = [];
    this.activeExam.forEach((item, idx) => {
      const selected = document.querySelector(`input[name="cert_q_${idx}"]:checked`);
      if (selected && parseInt(selected.value, 10) === item.answer) {
        correctCount++;
      } else {
        missed.push({ num: idx + 1, q: item.q, exp: item.explanation || 'Revisa los módulos pedagógicos de este nivel.' });
      }
    });

    const total = this.activeExam.length;
    const percentage = Math.round((correctCount / total) * 100);
    const passed = percentage >= 80;

    const examModal = document.getElementById('cert-exam-modal');

    if (passed) {
      if (examModal && examModal.open) examModal.close();
      this.showDiploma(this.currentLevel, percentage);
    } else {
      const feedbackEl = document.getElementById('cert-exam-feedback');
      if (feedbackEl) {
        const prefix = timedOut ? 'Tiempo agotado. ' : '';
        feedbackEl.hidden = false;
        feedbackEl.innerHTML = `
          <strong>${prefix}Examen no superado (${percentage}% — ${correctCount}/${total} aciertos).</strong>
          Se requiere al menos un 80% para acreditar.
          <ul style="margin:8px 0 0 18px; padding:0; font-size:0.84rem; line-height:1.45;">
            ${missed.map(m => `<li><strong>Pregunta ${m.num}:</strong> ${this.escape(m.exp)}</li>`).join('')}
          </ul>
        `;
        feedbackEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    }
  },

  updateDiplomaName(name) {
    const clean = String(name ?? '').trim() || 'SDET Software Engineer in Test';
    const display = document.getElementById('diploma-student-display');
    if (display) {
      display.textContent = clean;
    }
  },

  showDiploma(level, percentage) {
    const diplomaModal = document.getElementById('diploma-modal');
    const certName = (document.getElementById('diploma-student-name')?.value || '').trim() || 'SDET Software Engineer in Test';
    const dateStr = new Intl.DateTimeFormat('es-ES', { dateStyle: 'full' }).format(new Date());
    this.lastPercentage = percentage;
    this.currentCertId = `CERT-NORDVIK-${level.toUpperCase()}-${Math.floor(100000 + Math.random() * 900000)}`;
    const certId = this.currentCertId;

    const diplomaContainer = document.getElementById('diploma-render-area');
    if (diplomaContainer) {
      diplomaContainer.innerHTML = `
        <div class="diploma-card" style="position:relative; background:#0b1120; color:#f8fafc; border:4px double #38bdf8; border-radius:16px; padding:32px; box-shadow:0 12px 40px rgba(0,0,0,0.5); font-family:Georgia, serif; text-align:center;">
          <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:16px;">
            <div style="text-align:left;">
              <span style="font-size:0.75rem; letter-spacing:0.1em; color:#94a3b8; text-transform:uppercase;">Nordvik Financial Testing Academy</span>
              <h4 style="margin:2px 0 0 0; color:#38bdf8; font-size:1.1rem; font-family:-apple-system, sans-serif;">Söderberg &amp; Partners</h4>
            </div>
            <div style="background:rgba(56,189,248,0.1); border:1px solid #38bdf8; border-radius:8px; padding:6px 12px;">
              <span style="font-size:0.75rem; color:#38bdf8; font-weight:700; font-family:-apple-system, sans-serif;">CALIFICACIÓN: ${percentage}% (EXCELENCIA)</span>
            </div>
          </div>

          <div style="margin:24px 0 16px 0;">
            <p style="font-size:0.9rem; letter-spacing:0.15em; color:#94a3b8; text-transform:uppercase; margin-bottom:6px;">Certificado Oficial de Acreditación</p>
            <h1 style="font-size:1.8rem; margin:0; color:#f8fafc; font-weight:normal;">Maestría en Karate DSL &amp; Sistemas Actuariales</h1>
            <h2 style="font-size:1.25rem; color:#38bdf8; margin:8px 0 0 0; font-family:-apple-system, sans-serif; font-weight:700;">NIVEL: ${this.getLevelName(level).toUpperCase()}</h2>
          </div>

          <p style="font-size:0.95rem; color:#cbd5e1; margin:16px 0; font-style:italic;">
            Se certifica formalmente que el profesional
          </p>
          <div id="diploma-student-display" style="font-size:1.4rem; font-weight:bold; color:#fff; border-bottom:1px solid rgba(255,255,255,0.2); display:inline-block; padding:0 30px 4px 30px; margin-bottom:16px;">
            ${this.escape(certName)}
          </div>
          <p style="font-size:0.88rem; color:#cbd5e1; max-width:620px; margin:0 auto 24px auto; line-height:1.5;">
            Ha superado con éxito la evaluación de contratos HTTP, estructuras JSON, asincronía sin sleep, sagas distribuidas, idempotencia de pagos y mitigación de vulnerabilidades IDOR sobre la plataforma bancaria Nordvik.
          </p>

          <div style="display:flex; justify-content:space-between; align-items:flex-end; border-top:1px solid rgba(255,255,255,0.15); padding-top:16px; margin-top:20px;">
            <div style="text-align:left; font-size:0.75rem; color:#94a3b8; font-family:-apple-system, sans-serif;">
              <div><strong>FECHA DE EMISIÓN:</strong> ${dateStr}</div>
              <div><strong>CERTIFICATE ID:</strong> <code style="color:#38bdf8;">${certId}</code></div>
              <div><strong>VALIDACIÓN:</strong> SHA-256 DIGITAL EMBEDDED</div>
            </div>

            <div style="text-align:right;">
              <div style="font-family:'Brush Script MT', cursive; font-size:1.6rem; color:#38bdf8; margin-bottom:-4px;">Bruno SDET</div>
              <div style="font-size:0.75rem; color:#94a3b8; border-top:1px solid #38bdf8; padding-top:2px; font-family:-apple-system, sans-serif;">
                <strong>BRUNO SDET</strong> · Lead QA Architect
              </div>
            </div>
          </div>
        </div>
      `;
    }

    if (diplomaModal && !diplomaModal.open) diplomaModal.showModal();
  },

  getLevelName(lvl) {
    const map = {
      junior: 'Junior SDET',
      mid: 'Mid-Level SDET',
      senior: 'Senior SDET',
      staff: 'Staff / Lead SDET'
    };
    return map[lvl] || 'SDET';
  },

  escape(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { Certificacion };
}
