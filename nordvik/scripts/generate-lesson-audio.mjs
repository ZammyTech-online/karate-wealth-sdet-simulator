#!/usr/bin/env node
/**
 * Pipeline de Generación de Audio y Timestamps con ElevenLabs para la Academia Karate.
 * Sintetiza la explicación técnica en inglés con timestamps palabra a palabra y retos predictivos.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, '..');
const AUDIO_DIR = path.join(REPO_ROOT, 'torre', 'web', 'audio');

function dirname(p) {
  return path.dirname(p);
}

// 1. Obtener ELEVENLABS_API_KEY
function getApiKey() {
  if (process.env.ELEVENLABS_API_KEY) return process.env.ELEVENLABS_API_KEY.trim();
  const envPaths = [
    path.join('D:', 'workspace', 'fabrica-videos-core', '.env'),
    path.join(REPO_ROOT, '.env')
  ];
  for (const ep of envPaths) {
    if (fs.existsSync(ep)) {
      const content = fs.readFileSync(ep, 'utf8');
      const match = content.match(/ELEVENLABS_API_KEY\s*=\s*([^\r\n]+)/);
      if (match) return match[1].trim();
    }
  }
  throw new Error('No se encontró ELEVENLABS_API_KEY en variables de entorno ni en fabrica-videos-core/.env');
}

// 2. Banco de guiones narrativos adaptados para locución clara y técnica
const LESSON_SCRIPTS = {
  L00: {
    title: 'What is an API?',
    voiceId: 'TX3LPaxmHKxFdv7VOQHJ', // Liam - Energetic American SDET
    text: `Welcome to Lesson L00 of the Karate SDET Academy: What is an API? In this lesson, you will understand how programs communicate over HTTP and execute your very first automated test. An API is a doorway through which one software service requests actions from another. At Nordvik, each microservice is one of those doors. You, using Karate DSL, are the client. The microservice is the server. Every HTTP interaction consists of two halves: the request, where you specify the HTTP method like GET or POST, the target URL, and optional headers or JSON body; and the response, returned by the server with an HTTP status code, response headers, and the payload. Here is the golden contract of HTTP status codes. Two hundred series codes mean success. Two hundred OK means your request succeeded. Two hundred and one means a new resource was created. Four hundred series codes mean client error. Four hundred Bad Request means invalid input. Four hundred and one means unauthorized. Four hundred and four means not found. Four hundred and nine indicates a state conflict. Five hundred series codes mean server failure. In Karate, we express this deterministically. We specify Given url, And path, When method get, and Then status 200. A great SDET always predicts the exact status code before sending the request. Complete the gaps in the exercise editor to verify your understanding.`,
    challenges: [
      {
        id: 'c1',
        timeRatio: 0.28, // approx at 28% of narration
        prompt: 'Which HTTP method should be used to read data from a catalog without altering server state?',
        options: ['GET', 'POST', 'DELETE'],
        answerIndex: 0,
        explanation: 'Correct! GET is safe and idempotent, ideal for reading data.'
      },
      {
        id: 'c2',
        timeRatio: 0.62,
        prompt: 'According to HTTP standards, which status code indicates that the requested endpoint does not exist?',
        options: ['400 Bad Request', '404 Not Found', '500 Server Error'],
        answerIndex: 1,
        explanation: 'Spot on! 404 indicates the resource path was not found on the server.'
      },
      {
        id: 'c3',
        timeRatio: 0.88,
        prompt: 'In Karate DSL, how do we assert that the response status code equals 200?',
        options: ['* sleep 200', 'Then status 200', 'assert status = 200'],
        answerIndex: 1,
        explanation: 'Excellent! "Then status 200" is Karate\'s built-in declarative assertion.'
      }
    ]
  },
  L01: {
    title: 'Collections and JSON Arrays',
    voiceId: 'TX3LPaxmHKxFdv7VOQHJ',
    text: `Welcome to Lesson L01: Collections and Arrays in Karate DSL. When querying banking portfolios and fund catalogs, services return JSON lists. In Karate, asserting collections requires understanding array lengths and member schemas. We use the match response.items equals hash open close brackets assertion to verify that a payload is an array. To check that every element contains required fields, Karate provides the each wildcard. Remember, never loop manually with JavaScript when Karate native assertions can validate the entire collection in a single line.`,
    challenges: [
      {
        id: 'c1',
        timeRatio: 0.45,
        prompt: 'Which Karate fuzzy matcher asserts that a field contains a JSON array?',
        options: ['#[]', '#string', '#number'],
        answerIndex: 0,
        explanation: 'Correct! #[] matches any JSON array.'
      }
    ]
  },
  L02: {
    title: 'Fuzzy Matchers and Strict Schemas',
    voiceId: 'TX3LPaxmHKxFdv7VOQHJ',
    text: `Welcome to Lesson L02: Fuzzy Matchers and Schema Validation. In financial testing, values like timestamps, transaction IDs, and balances change dynamically on every run. Hardcoding exact values leads to flaky tests. Karate solves this with fuzzy matchers like hash string, hash number, and hash regex. By defining an exact schema contract, our suite validates the structure without becoming brittle. Always enforce type safety on every monetary field.`,
    challenges: [
      {
        id: 'c1',
        timeRatio: 0.50,
        prompt: 'Why do we avoid hardcoding dynamic identifiers in Karate assertions?',
        options: ['It causes test flakiness across runs', 'Karate does not support strings', 'It slows down HTTP requests'],
        answerIndex: 0,
        explanation: 'Exact! Dynamic IDs change across test runs and must be asserted using schemas.'
      }
    ]
  }
};

// 3. Agrupador de caracteres a palabras con timestamps
function buildWordAlignment(alignment) {
  if (!alignment || !alignment.characters) return [];
  const chars = alignment.characters;
  const starts = alignment.character_start_times_seconds;
  const ends = alignment.character_end_times_seconds;

  const words = [];
  let currentWord = '';
  let wordStart = null;
  let wordEnd = null;

  for (let i = 0; i < chars.length; i++) {
    const c = chars[i];
    const isWhitespaceOrPunct = /[\s\r\n]/.test(c);

    if (!isWhitespaceOrPunct) {
      if (wordStart === null) wordStart = starts[i];
      wordEnd = ends[i];
      currentWord += c;
    } else {
      if (currentWord.length > 0) {
        words.push({
          word: currentWord.replace(/[^\w'-]/g, ''),
          raw: currentWord,
          start: Number(wordStart.toFixed(3)),
          end: Number(wordEnd.toFixed(3))
        });
        currentWord = '';
        wordStart = null;
        wordEnd = null;
      }
    }
  }

  if (currentWord.length > 0 && wordStart !== null) {
    words.push({
      word: currentWord.replace(/[^\w'-]/g, ''),
      raw: currentWord,
      start: Number(wordStart.toFixed(3)),
      end: Number(wordEnd.toFixed(3))
    });
  }

  return words;
}

// 4. Función de síntesis con ElevenLabs
async function synthesizeLesson(lessonId, force = false) {
  const lesson = LESSON_SCRIPTS[lessonId];
  if (!lesson) {
    console.error(`Lección ${lessonId} no registrada en LESSON_SCRIPTS`);
    return false;
  }

  fs.mkdirSync(AUDIO_DIR, { recursive: true });
  const mp3Path = path.join(AUDIO_DIR, `${lessonId}.mp3`);
  const jsonPath = path.join(AUDIO_DIR, `${lessonId}.json`);

  if (!force && fs.existsSync(mp3Path) && fs.existsSync(jsonPath)) {
    console.log(`✓ [Cache Hit] Audio y timestamps para ${lessonId} ya existen en disco.`);
    return true;
  }

  const apiKey = getApiKey();
  console.log(`Generando audio para ${lessonId} con ElevenLabs (Voz: ${lesson.voiceId}, ${lesson.text.length} chars)...`);

  const url = `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(lesson.voiceId)}/with-timestamps?output_format=mp3_44100_128`;
  const res = await fetch(url, {
    method: 'POST',
    headers: {
      'xi-api-key': apiKey,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      text: lesson.text,
      model_id: 'eleven_multilingual_v2',
      voice_settings: {
        stability: 0.55,
        similarity_boost: 0.8,
        use_speaker_boost: true
      }
    })
  });

  if (!res.ok) {
    const errText = await res.text();
    throw new Error(`ElevenLabs API Error ${res.status}: ${errText}`);
  }

  const data = await res.json();
  const audioBuffer = Buffer.from(data.audio_base64, 'base64');
  fs.writeFileSync(mp3Path, audioBuffer);

  const words = buildWordAlignment(data.alignment);
  const totalDuration = words.length > 0 ? words[words.length - 1].end : 0;

  // Asignar tiempos reales a los retos predictivos basados en timeRatio
  const challenges = (lesson.challenges || []).map(ch => ({
    ...ch,
    time: Number((totalDuration * ch.timeRatio).toFixed(2))
  }));

  const metadata = {
    lessonId,
    title: lesson.title,
    voiceId: lesson.voiceId,
    language: 'en',
    duration: Number(totalDuration.toFixed(2)),
    wordsCount: words.length,
    words,
    challenges
  };

  fs.writeFileSync(jsonPath, JSON.stringify(metadata, null, 2), 'utf8');
  console.log(`✓ [Guardado] ${mp3Path} (${(audioBuffer.length / 1024).toFixed(1)} KB, ${metadata.duration}s, ${words.length} palabras)`);
  console.log(`✓ [Guardado] ${jsonPath} (${challenges.length} retos predictivos)`);
  return true;
}

// 5. Ejecución principal CLI
const targetLesson = process.argv[2] || 'L00';

async function main() {
  try {
    if (targetLesson === '--all') {
      for (const id of Object.keys(LESSON_SCRIPTS)) {
        await synthesizeLesson(id);
      }
    } else {
      await synthesizeLesson(targetLesson);
    }
    console.log('Generación completada con éxito.');
  } catch (err) {
    console.error('Error durante la generación:', err.message);
    process.exit(1);
  }
}

main();
