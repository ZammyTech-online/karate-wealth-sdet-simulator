// BrunoHeuristics: Motor heurístico determinista y local (0 tokens, 100% offline).
// Diagnostica errores de Karate DSL, códigos HTTP RFC 9457 y excepciones de Java/GraalVM.
'use strict';

const BrunoHeuristics = {
  diagnose(rawError, context = {}) {
    if (!rawError || typeof rawError !== 'string') {
      return {
        status: 'OK',
        title: 'Todo en orden',
        message: 'No se han detectado errores en la salida de Karate.',
        avatarState: 'SUCCESS',
        hint: 'Puedes avanzar a la siguiente lección o probar casos límites en practica.feature.'
      };
    }

    const err = rawError;

    // 1. Error de Variable no definida / ReferenceError
    if (err.includes('ReferenceError:') || err.includes('is not defined')) {
      const match = err.match(/ReferenceError:\s*"?([^"'\s]+)"?\s+is not defined/);
      const varName = match ? match[1] : 'una variable';
      return {
        status: 'ERROR',
        title: `Variable '${varName}' no declarada`,
        message: `Karate intentó evaluar '${varName}', pero no existe en el contexto.`,
        avatarState: 'ALERT',
        hint: `Define '${varName}' antes de usarla con '* def ${varName} = ...' o revisa si olvidaste declararla en el Background.`
      };
    }

    // 2. Error de Sintaxis en Match de Comparación
    if (err.includes("expected '==' for match") || err.includes("no step-definition method match found for: match")) {
      return {
        status: 'ERROR',
        title: 'Uso incorrecto de match para comparación numérica',
        message: "En Karate DSL no se usa 'match x < y'. La aserción 'match' solo evalúa igualdad o estructuras.",
        avatarState: 'EXPLAINING',
        hint: "Usa la instrucción nativa 'assert' para comparaciones booleanas relativas: '* assert latency < 200' o '* assert balance >= 1000'."
      };
    }

    // 3. Error HTTP 401 Unauthorized
    if (err.includes('status: 401') || err.includes('401 Unauthorized')) {
      return {
        status: 'ERROR',
        title: '401 Unauthorized · Sesión No Autenticada',
        message: 'El microservicio bancario denegó el acceso porque la petición no incluye credenciales válidas.',
        avatarState: 'ALERT',
        hint: "Asegúrate de invocar '_login.feature' y adjuntar la cabecera 'Authorization: Bearer <token>' en tu petición."
      };
    }

    // 4. Error HTTP 403 Forbidden (RBAC / Permisos)
    if (err.includes('status: 403') || err.includes('403 Forbidden')) {
      return {
        status: 'ERROR',
        title: '403 Forbidden · Violación de Roles (RBAC)',
        message: 'Tu token es válido, pero el usuario no tiene los privilegios requeridos para esta operación actuarial.',
        avatarState: 'ALERT',
        hint: "Verifica qué rol exige el endpoint (ej. 'Advisor', 'ComplianceOfficer' o 'Backoffice'). Los clientes no pueden autorizar traspasos directos."
      };
    }

    // 5. Error HTTP 409 Conflict / Idempotencia
    if (err.includes('status: 409') || err.includes('409 Conflict')) {
      return {
        status: 'ERROR',
        title: '409 Conflict · Violación de Idempotencia o Estado',
        message: 'La transacción colisionó con una orden previa o el recurso ya cambió a un estado incompatible.',
        avatarState: 'EXPLAINING',
        hint: "Revisa la cabecera 'Idempotency-Key' o verifica si la saga de pensión ya fue confirmada previamente."
      };
    }

    // 6. Error de Rutas Relativas en Karate (FileNotFoundException)
    if (err.includes('FileNotFoundException') || err.includes('The system cannot find the file specified')) {
      return {
        status: 'ERROR',
        title: 'Recurso o Feature no encontrado',
        message: 'Karate intentó leer un archivo llamable (.feature, .json o .js) que no está en la ruta indicada.',
        avatarState: 'THINKING',
        hint: "Si usas 'this:_ayudante.feature', asegúrate de que el archivo esté en la misma carpeta del feature activo, o usa 'classpath:...' para rutas absolutas del paquete."
      };
    }

    // 7. Error de agotamiento de puertos (BindException en Windows)
    if (err.includes('BindException') || err.includes('Address already in use')) {
      return {
        status: 'ERROR',
        title: 'Agotamiento de sockets efímeros TCP',
        message: 'La ejecución paralela saturó el rango de puertos locales de Windows en estado TIME_WAIT.',
        avatarState: 'ALERT',
        hint: "Limita los hilos concurrentes del runner a 1 o 2 en desarrollo local con '-Dkarate.threads=1'."
      };
    }

    // 8. Huecos ___ sin completar
    if (err.includes('___')) {
      return {
        status: 'ERROR',
        title: 'Hueco sin completar en el ejercicio',
        message: 'Aún quedan marcas de hueco triple guion bajo (___) en el escenario.',
        avatarState: 'EXPLAINING',
        hint: "Sustituye cada '___' por el valor o palabra clave correcta indicada en el enunciado y la pista."
      };
    }

    // Fallback general
    return {
      status: 'ERROR',
      title: 'Fallo de Aserción o Respuesta Inesperada',
      message: 'Uno de los pasos de validación no coincidió con el resultado retornado por el microservicio.',
      avatarState: 'THINKING',
      hint: 'Revisa el reporte HTML generado en target/academia/ para ver los headers y body exactos de la petición HTTP.'
    };
  }
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { BrunoHeuristics };
}
