const crypto = require('crypto');

const ROOM_CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const ROOM_CODE_LENGTH = 6;

const TRACK_IDS = ['forest', 'desert', 'city'];
const LAP_OPTIONS = [1, 2, 3, 5, 7, 10];
const TOTAL_CAR_OPTIONS = [4, 6, 8, 10, 12, 16];
const DIFFICULTIES = ['easy', 'medium', 'hard', 'expert'];
const CAR_MODELS = ['falcon', 'titan', 'storm'];
const CAR_COLORS = 8;

const MAX_HUMANS = 8;
const MAX_POSITION_JUMP = 30;

const DEFAULT_SETTINGS = Object.freeze({
  trackId: 'forest',
  laps: 3,
  totalCars: 8,
  difficulty: 'medium',
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

function sanitizeSettings(input, current = DEFAULT_SETTINGS) {
  const out = { ...current };
  if (!input || typeof input !== 'object') return out;
  if (TRACK_IDS.includes(input.trackId)) out.trackId = input.trackId;
  if (LAP_OPTIONS.includes(Number(input.laps))) out.laps = Number(input.laps);
  if (TOTAL_CAR_OPTIONS.includes(Number(input.totalCars))) out.totalCars = Number(input.totalCars);
  if (DIFFICULTIES.includes(input.difficulty)) out.difficulty = input.difficulty;
  return out;
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

function compactState(id, s) {
  return {
    id,
    x: round2(s.x),
    y: round2(s.y),
    z: round2(s.z),
    rotation: Math.round(s.rotation),
    speed: Math.round(s.speed * 10) / 10,
    lap: s.lap,
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
  LAP_OPTIONS,
  TOTAL_CAR_OPTIONS,
  DIFFICULTIES,
  CAR_MODELS,
  MAX_HUMANS,
  MAX_POSITION_JUMP,
  DEFAULT_SETTINGS,
  generateRoomCode,
  generateId,
  normalizeRoomCode,
  sanitizeName,
  sanitizeSettings,
  sanitizeCar,
  isFiniteNumber,
  compactState,
  distance,
  shuffle,
};
