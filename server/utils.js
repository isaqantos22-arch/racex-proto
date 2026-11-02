const crypto = require('crypto');

const ROOM_CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const ROOM_CODE_LENGTH = 6;

const TRACK_IDS = ['forest', 'desert', 'city', 'velocita', 'principado', 'ardenne', 'jardim', 'golfo', 'serra', 'sol', 'parque', 'dragao', 'corniche', 'porto', 'cascatas', 'dourado', 'colinas', 'norte', 'danubio', 'dunas', 'fortaleza', 'texas', 'estadio', 'neon'];
// Voltas: 0 = voltas REAIS da pista (o cliente calcula), ou qualquer inteiro de 1 a 200 (o jogador digita).
const MIN_LAPS = 0;
const MAX_LAPS = 200;
// Carros no grid (humanos + IA): igual ao Single Player, até as 24 vagas do grid.
const MIN_CARS = 2;
const MAX_CARS = 24;
const DIFFICULTIES = ['easy', 'medium', 'hard', 'expert'];
const CATEGORIES = ['gt', 'f1'];
// Clima (mesmos ids do WeatherSystem do cliente; '' = seco).
const WEATHERS = ['', 'dry', 'cloudy', 'light', 'heavy', 'mixed'];
// Mesmos nomes do enum CarModel do cliente (minúsculo).
const CAR_MODELS = ['falcon', 'titan', 'storm', 'f1', 'ferrari', 'r8', 'mustang', 'revuelto', 'gtrnismo', 'mclarenp1', 'bugatti', 'f2008', 'f1studio', 'f1legacy', 'f1basic', 'f1concept26', 'f1custom22', 'mclarenf1', 'ferrarif60', 'f1sponsor', 'lotus77', 'fp04', 'f4british', 'mclarenmp45', 'f1photo22', 'f1carconcept', 'f1y2026', 'formulac1', 'formulac2'];
const CAR_COLORS = 16;

const MAX_HUMANS = 8;
const MAX_POSITION_JUMP = 30;

const DEFAULT_SETTINGS = Object.freeze({
  trackId: 'forest',
  laps: 3,
  totalCars: 8,
  difficulty: 'medium',
  category: 'gt',
  pitStops: false,
  weather: '',
  // Regras da corrida escolhidas pelo anfitrião (iguais para todos): perda total e penalidades da direção de prova.
  totalLoss: true,
  penalties: true,
});

function generateRoomCode(exists = () => false) {
  for (let attempt = 0; attempt < 1000; attempt++) {
    let code = '';
    const bytes = crypto.randomBytes(ROOM_CODE_LENGTH);
    for (let i = 0; i < ROOM_CODE_LENGTH; i++) {
      code += ROOM_CODE_ALPHABET[bytes[i] % ROOM_CODE_ALPHABET.length];
    }
    if (!exists(code)) return code;
  }
  throw new Error('Não foi possível gerar código de sala único');
}

function generateId(prefix) {
  return `${prefix}_${crypto.randomBytes(6).toString('hex')}`;
}

function normalizeRoomCode(code) {
  if (typeof code !== 'string') return null;
  const c = code.trim().toUpperCase();
  if (c.length !== ROOM_CODE_LENGTH) return null;
  for (const ch of c) if (!ROOM_CODE_ALPHABET.includes(ch)) return null;
  return c;
}

function sanitizeName(name, fallback = 'Piloto') {
  if (typeof name !== 'string') return fallback;
  const clean = name.replace(/[\u0000-\u001f<>]/g, '').trim().slice(0, 16);
  return clean.length > 0 ? clean : fallback;
}

/** Aplica as configurações válidas e devolve também a lista do que foi RECUSADO (o cliente avisa o host). */
function sanitizeSettingsDetailed(input, current = DEFAULT_SETTINGS) {
  const out = { ...DEFAULT_SETTINGS, ...current };
  const rejected = [];
  if (!input || typeof input !== 'object') return { settings: out, rejected };
  if (input.trackId !== undefined) {
    if (TRACK_IDS.includes(input.trackId)) out.trackId = input.trackId;
    else rejected.push('pista');
  }
  if (input.laps !== undefined) {
    const laps = Number(input.laps);
    if (Number.isInteger(laps) && laps >= MIN_LAPS && laps <= MAX_LAPS) out.laps = laps;
    else rejected.push('voltas');
  }
  if (input.totalCars !== undefined) {
    const cars = Number(input.totalCars);
    if (Number.isInteger(cars) && cars >= MIN_CARS && cars <= MAX_CARS) out.totalCars = cars;
    else rejected.push('carros');
  }
  if (input.difficulty !== undefined) {
    if (DIFFICULTIES.includes(input.difficulty)) out.difficulty = input.difficulty;
    else rejected.push('dificuldade');
  }
  if (input.category !== undefined) {
    if (CATEGORIES.includes(input.category)) out.category = input.category;
    else rejected.push('categoria');
  }
  if (input.weather !== undefined) {
    if (WEATHERS.includes(input.weather)) out.weather = input.weather;
    else rejected.push('clima');
  }
  if (input.pitStops !== undefined) {
    if (typeof input.pitStops === 'boolean') out.pitStops = input.pitStops;
    else rejected.push('pit stop');
  }
  if (input.totalLoss !== undefined) {
    if (typeof input.totalLoss === 'boolean') out.totalLoss = input.totalLoss;
    else rejected.push('perda total');
  }
  if (input.penalties !== undefined) {
    if (typeof input.penalties === 'boolean') out.penalties = input.penalties;
    else rejected.push('penalidades');
  }
  return { settings: out, rejected };
}

function sanitizeSettings(input, current = DEFAULT_SETTINGS) {
  return sanitizeSettingsDetailed(input, current).settings;
}

function sanitizeCar(input, current) {
  const out = { ...current };
  if (!input || typeof input !== 'object') return out;
  if (CAR_MODELS.includes(input.carModel)) out.carModel = input.carModel;
  const color = Number(input.carColor);
  if (Number.isInteger(color) && color >= 0 && color < CAR_COLORS) out.carColor = color;
  return out;
}

function isFiniteNumber(v) {
  return typeof v === 'number' && Number.isFinite(v);
}

function round2(v) {
  return Math.round(v * 100) / 100;
}

/** st: 0 = correndo, 1 = cruzou a linha final, 2 = abandonou (perda total/quebra/desclassificado) — vem logo depois de
 * "lap" porque o cliente lê o pacote por expressão regular nessa ordem. */
function compactState(id, s) {
  return {
    id,
    x: round2(s.x),
    y: round2(s.y),
    z: round2(s.z),
    rotation: Math.round(s.rotation),
    speed: Math.round(s.speed * 10) / 10,
    lap: s.lap,
    st: s.finished ? 1 : s.retired ? 2 : 0,
  };
}

function distance(a, b) {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  const dz = a.z - b.z;
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}

function shuffle(arr) {
  const a = arr.slice();
  for (let i = a.length - 1; i > 0; i--) {
    const j = crypto.randomInt(i + 1);
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

module.exports = {
  ROOM_CODE_ALPHABET,
  ROOM_CODE_LENGTH,
  TRACK_IDS,
  MIN_LAPS,
  MAX_LAPS,
  MIN_CARS,
  MAX_CARS,
  DIFFICULTIES,
  CATEGORIES,
  CAR_MODELS,
  MAX_HUMANS,
  MAX_POSITION_JUMP,
  DEFAULT_SETTINGS,
  generateRoomCode,
  generateId,
  normalizeRoomCode,
  sanitizeName,
  sanitizeSettings,
  sanitizeSettingsDetailed,
  sanitizeCar,
  isFiniteNumber,
  compactState,
  distance,
  shuffle,
};
