const http = require('http');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');

const PORT = 3000;
const SUITE_ROOT = 'D:\\WORKSPACE_KARATE\\karate-test-suite';
const DATA_FILE = path.join(__dirname, 'data', 'curriculum.json');

function scanKarateFeatures(dir, results = []) {
  if (!fs.existsSync(dir)) return results;
  const list = fs.readdirSync(dir);
  list.forEach(file => {
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      scanKarateFeatures(fullPath, results);
    } else if (file.endsWith('.feature') || file.endsWith('.java') || file.endsWith('.js')) {
      const content = fs.readFileSync(fullPath, 'utf8');
      const lines = content.split('\n');
      
      const calls = [];
      const javaTypes = [];
      const defs = [];

      lines.forEach((l, idx) => {
        const line = l.trim();
        const callMatch = line.match(/karate\.call(once)?\(['"]([^'"]+)['"](?:,\s*([^)]+))?\)/);
        if (callMatch) {
          calls.push({
            type: callMatch[1] ? 'callonce' : 'call',
            target: callMatch[2],
            args: callMatch[3] || null,
            line: idx + 1
          });
        }
        const javaMatch = line.match(/Java\.type\(['"]([^'"]+)['"]\)/);
        if (javaMatch) {
          javaTypes.push({ class: javaMatch[1], line: idx + 1 });
        }
        const defMatch = line.match(/\*\s+def\s+([a-zA-Z0-9_$]+)\s*=/);
        if (defMatch) {
          defs.push({ variable: defMatch[1], line: idx + 1 });
        }
      });

      results.push({
        name: file,
        relativePath: path.relative(SUITE_ROOT, fullPath).replace(/\\/g, '/'),
        calls,
        javaTypes,
        defs,
        rawContent: content
      });
    }
  });
  return results;
}

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(200);
    res.end();
    return;
  }

  const url = new URL(req.url, `http://localhost:${PORT}`);

  // 1. Scan Structure
  if (url.pathname === '/api/scan') {
    const files = scanKarateFeatures(path.join(SUITE_ROOT, 'src', 'test'));
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ files }));
    return;
  }

  // 2. Fetch Curriculum (Lessons, Quizzes, Challenges, Logic Gym)
  if (url.pathname === '/api/curriculum') {
    if (fs.existsSync(DATA_FILE)) {
      const data = fs.readFileSync(DATA_FILE, 'utf8');
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(data);
    } else {
      res.writeHead(404, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'Curriculum not found' }));
    }
    return;
  }

  // 3. Evaluate Karate Challenge in Real-Time
  if (url.pathname === '/api/verify-karate' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => body += chunk);
    req.on('end', () => {
      try {
        const { id, code } = JSON.parse(body);
        const curriculum = JSON.parse(fs.readFileSync(DATA_FILE, 'utf8'));
        const module = curriculum.karateModules.find(m => m.id === id);

        if (!module) {
          res.writeHead(404, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Module not found' }));
          return;
        }

        // Write temp feature file
        const tempFeaturePath = path.join(SUITE_ROOT, 'src', 'test', 'java', 'com', 'soderberg', 'qa', 'challenges', `challenge_${id}.feature`);
        fs.writeFileSync(tempFeaturePath, code, 'utf8');

        // Execute via Karate CLI / Maven
        const regex = new RegExp(module.challenge.solutionRegex, 's');
        const matchesSolutionPattern = regex.test(code);

        if (!matchesSolutionPattern) {
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({
            passed: false,
            feedback: "El código no cumple con los pasos obligatorios o aserciones esperadas del reto. Revisa las instrucciones y la teoría de la lección.",
            matchesPattern: false
          }));
          return;
        }

        // Run this specific feature using Maven
        const testCmd = `mvn test -Dtest=WealthRegressionRunner -Dkarate.options="classpath:com/soderberg/qa/challenges/challenge_${id}.feature"`;
        exec(testCmd, { cwd: SUITE_ROOT, timeout: 25000 }, (error, stdout) => {
          const success = !error;
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({
            passed: success,
            feedback: success 
              ? "🎉 ¡Excelente trabajo de SDET Senior! Tu feature se ejecutó contra el backend .NET en vivo y todas las aserciones pasaron limpiamente." 
              : "❌ El feature falló en la ejecución contra el backend en vivo:\n" + stdout.split('\n').slice(-15).join('\n'),
            rawOutput: stdout.split('\n').slice(-20).join('\n')
          }));
        });

      } catch (err) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: err.message }));
      }
    });
    return;
  }

  // 4. Evaluate Logic Gym Challenge
  if (url.pathname === '/api/verify-logic' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => body += chunk);
    req.on('end', () => {
      try {
        const { id, code } = JSON.parse(body);
        const curriculum = JSON.parse(fs.readFileSync(DATA_FILE, 'utf8'));
        const problem = curriculum.logicGym.find(l => l.id === id);

        if (!problem) {
          res.writeHead(404, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Problem not found' }));
          return;
        }

        // Evaluate code in safe sandbox VM
        const vm = require('vm');
        const context = vm.createContext({});
        vm.runInContext(code, context);

        // Find function name from problem.initialCode
        const fnMatch = problem.initialCode.match(/function\s+([a-zA-Z0-9_$]+)/);
        const fnName = fnMatch ? fnMatch[1] : null;

        if (!fnName || typeof context[fnName] !== 'function') {
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({
            passed: false,
            feedback: `No se encontró la función '${fnName}' definida correctamente en tu solución.`
          }));
          return;
        }

        let allPassed = true;
        const failedTests = [];

        problem.testCases.forEach((tc, idx) => {
          try {
            const actual = Array.isArray(tc.input) 
              ? context[fnName](...tc.input)
              : context[fnName](tc.input);

            const equals = JSON.stringify(actual) === JSON.stringify(tc.expected);
            if (!equals) {
              allPassed = false;
              failedTests.push({ testIndex: idx + 1, input: tc.input, expected: tc.expected, actual });
            }
          } catch (e) {
            allPassed = false;
            failedTests.push({ testIndex: idx + 1, error: e.message });
          }
        });

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          passed: allPassed,
          feedback: allPassed 
            ? "⭐ ¡Perfecto! Tu función superó todos los casos de prueba de forma determinística." 
            : `Fallaron ${failedTests.length} de ${problem.testCases.length} casos de prueba.`,
          details: failedTests
        }));

      } catch (err) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ passed: false, feedback: "Error de sintaxis: " + err.message }));
      }
    });
    return;
  }

  // 5. Run Full Regression
  if (url.pathname === '/api/run-tests' && req.method === 'POST') {
    exec('mvn test -Dtest=WealthRegressionRunner', { cwd: SUITE_ROOT }, (error, stdout) => {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({
        success: !error,
        stdout: stdout.split('\n').slice(-30).join('\n'),
        exitCode: error ? error.code : 0
      }));
    });
    return;
  }

  // 6. Report Status
  if (url.pathname === '/api/report-status') {
    const reportPath = path.join(SUITE_ROOT, 'target', 'karate-reports', 'karate-summary.html');
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ exists: fs.existsSync(reportPath), path: reportPath.replace(/\\/g, '/') }));
    return;
  }

  // Static files or fallback index.html
  let filePath = path.join(__dirname, 'public', url.pathname === '/' ? 'index.html' : url.pathname);
  if (!fs.existsSync(filePath)) {
    filePath = path.join(__dirname, 'public', 'index.html');
  }

  const ext = path.extname(filePath);
  const mimeMap = {
    '.html': 'text/html',
    '.css': 'text/css',
    '.js': 'application/javascript',
    '.json': 'application/json'
  };

  fs.readFile(filePath, (err, content) => {
    if (err) {
      res.writeHead(404);
      res.end('File not found');
    } else {
      res.writeHead(200, { 'Content-Type': mimeMap[ext] || 'text/plain' });
      res.end(content);
    }
  });
});

server.listen(PORT, () => {
  console.log(`🤖 Karate Master Academy listening on http://localhost:${PORT}`);
});
