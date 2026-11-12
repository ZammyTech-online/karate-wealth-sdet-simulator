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
    });

    return issues;
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoLinter };
}
