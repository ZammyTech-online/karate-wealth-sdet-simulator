// BrunoLinter: Inspector estático de buenas prácticas SDET para Karate DSL.
// Audita código en tiempo real para prevenir tests frágiles, sleeps y datos hardcodeados.
'use strict';

const BrunoLinter = {
  audit(featureCode) {
    const issues = [];
    if (!featureCode || typeof featureCode !== 'string') return issues;

    const lines = featureCode.split('\n');

    lines.forEach((line, idx) => {
      const lineNum = idx + 1;
      const trimmed = line.trim();

      // 1. Detección de sleep() o Thread.sleep()
      if (/karate\.sleep|Thread\.sleep|java\.lang\.Thread\.sleep/i.test(trimmed)) {
        issues.push({
          line: lineNum,
          level: 'CRITICAL',
          rule: 'ANTI_PATTERN_SLEEP',
          title: 'Uso de sleep() detectado',
          message: 'Dormir el hilo introduce lentitud y flakiness en pipelines CI/CD.',
          solution: "Usa el patrón reactivo 'retry until <condición>' o adelanta el reloj con clock-service."
        });
      }

      // 2. Detección de DNI / PNR sueco hardcodeado
      if (/\b(19\d{6}-\d{4}|20\d{6}-\d{4})\b/.test(trimmed)) {
        issues.push({
          line: lineNum,
          level: 'WARNING',
          rule: 'HARDCODED_PNR',
          title: 'Número personal nacional hardcodeado',
          message: 'Hardcodear identidades colisiona con el aislamiento de pruebas concurrentes.',
          solution: "Invoca '_persona.feature' o llama a synthetic-person-service para obtener identidades frescas."
        });
      }

      // 3. Detección de IBAN hardcodeado
      if (/\bSE\d{2}[0-9A-Z]{12,20}\b/i.test(trimmed)) {
        issues.push({
          line: lineNum,
          level: 'WARNING',
          rule: 'HARDCODED_IBAN',
          title: 'IBAN bancario fijo detectado',
          message: 'Las cuentas bancarias estáticas sufren colisiones de balance en pruebas paralelas.',
          solution: "Usa '_iban.feature' para generar números de cuenta actuariales validados con módulo 97."
        });
      }

      // 4. Detección de comparación numérica errónea con match
      if (/^\*\s*match\s+[^=!<>]+\s*[<>]=?\s*[^=!<>]+\s*$/.test(trimmed)) {
        issues.push({
          line: lineNum,
          level: 'CRITICAL',
          rule: 'INVALID_MATCH_COMPARISON',
          title: "Sintaxis 'match <' no soportada",
          message: "Karate no soporta operadores de comparación menor/mayor con 'match'.",
          solution: "Reemplaza por 'assert variable < umbral'."
        });
      }

      // 5. Variables no interpoladas en strings JSON
      if (/request\s*\{[^}]*:\s*req[A-Z0-9_]+[^}]*\}/i.test(trimmed) && !trimmed.includes("'#(")) {
        issues.push({
          line: lineNum,
          level: 'INFO',
          rule: 'UNQUOTED_VARIABLE_SUBSTITUTION',
          title: 'Posible variable sin interpolar en cuerpo JSON',
          message: 'En cuerpos JSON literales, las variables de Karate requieren la sintaxis \'#(nombreVar)\'.',
          solution: "Escribe: request { campo: '#(miVariable)' } para asegurar la sustitución de valores."
        });
      }

      // 6. Aserción colocada incorrectamente dentro de la fase Given
      if (/^Given\s+(match|assert)\s+/i.test(trimmed)) {
        issues.push({
          line: lineNum,
          level: 'CRITICAL',
          rule: 'GHERKIN_ASSERTION_IN_GIVEN',
          title: "Aserción en fase 'Given'",
          message: "'Given' es exclusivo para preparar la petición (Arrange). Las aserciones pertenecen a 'Then' o 'And'.",
          solution: "Mueve la validación después del 'When method' usando 'Then status ...' y 'And match ...'."
        });
      }

      // 7. Uso de '*' en aserciones de contrato en lugar de 'Then' o 'And'
      if (/^\*\s+status\s+\d+/i.test(trimmed)) {
        issues.push({
          line: lineNum,
          level: 'INFO',
          rule: 'GHERKIN_STAR_IN_STATUS',
          title: "Uso de '*' para validar status HTTP",
          message: "En metodología BDD, el código de respuesta es la aserción principal de la fase 'Act'.",
          solution: "Usa 'Then status <código>' para que la prueba documente claramente el contrato."
        });
      }
    });

    // Análisis semántico a nivel de bloques de escenario
    const scenarios = featureCode.split(/Scenario(?: Outline)?:\s*/i).slice(1);
    scenarios.forEach((sc, sIdx) => {
      const whenMatches = sc.match(/^\s*When\s+method\s+\w+/gim) || [];
      if (whenMatches.length > 1) {
        issues.push({
          line: 1,
          level: 'WARNING',
          rule: 'GHERKIN_MULTIPLE_WHEN_METHODS',
          title: `Múltiples métodos 'When' en escenario ${sIdx + 1}`,
          message: `El escenario contiene ${whenMatches.length} disparos HTTP ('When method'). Viola el principio de prueba atómica.`,
          solution: "Divide el flujo en dos escenarios o encapsula la preparación previa con '* call read(...) '."
        });
      }

      if (/^\s*When\s+method/im.test(sc) && !/Then\s+status\s+\d+/im.test(sc) && !/\*\s+status\s+\d+/im.test(sc)) {
        issues.push({
          line: 1,
          level: 'WARNING',
          rule: 'GHERKIN_MISSING_THEN_STATUS',
          title: `Falta aserción de status en escenario ${sIdx + 1}`,
          message: "Tras disparar un 'When method', siempre se debe validar el código HTTP esperado.",
          solution: "Añade 'Then status 200' (o el código que corresponda al caso de prueba)."
        });
      }

      if (/Scenario Outline:/i.test(featureCode) && !/Examples:\s*\|/i.test(featureCode)) {
        issues.push({
          line: 1,
          level: 'CRITICAL',
          rule: 'GHERKIN_SCENARIO_OUTLINE_NO_EXAMPLES',
          title: "Scenario Outline sin tabla 'Examples:'",
          message: "Un 'Scenario Outline' exige una tabla con cabecera y filas delimitadas por barras verticales '|'.",
          solution: "Añade el bloque 'Examples:' con los parámetros del caso de prueba."
        });
      }
    });

    return issues;
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoLinter };
}
