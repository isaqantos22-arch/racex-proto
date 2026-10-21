const { isFiniteNumber, compactState, distance, shuffle, MAX_POSITION_JUMP } = require('./utils');

const TICK_MS = 50;
const START_DELAY_MS = 4000;
const FINISH_GRACE_MS = 30000;

class GameSession {
  constructor(room, emit, options = {}) {
    this.room = room;
    this.emit = emit;
    this.tickMs = options.tickMs ?? TICK_MS;
    this.startDelayMs = options.startDelayMs ?? START_DELAY_MS;
    this.finishGraceMs = options.finishGraceMs ?? FINISH_GRACE_MS;
    this.states = new Map();
    this.finishOrder = [];
    this.timers = [];
    this.loop = null;
    this.startAt = 0;
    this.ended = false;
  }

  start() {
    const humans = [...this.room.players.values()];
    const settings = this.room.settings;
    const totalCars = Math.max(settings.totalCars, humans.length);
    const slots = shuffle([...Array(totalCars).keys()]);

    const startPositions = [];
    humans.forEach((p, i) => {
      startPositions.push({ id: p.id, slot: slots[i], isAI: false, name: p.name, carModel: p.carModel, carColor: p.carColor });
    });
    for (let i = humans.length; i < totalCars; i++) {
      startPositions.push({ id: `ai_${i - humans.length}`, slot: slots[i], isAI: true });
    }
    startPositions.sort((a, b) => a.slot - b.slot);

    for (const sp of startPositions) {
      this.states.set(sp.id, { isAI: sp.isAI, x: 0, y: 0, z: 0, rotation: 0, speed: 0, lap: 1, checkpoint: 0, hasPosition: false, finished: false });
    }

    this.startAt = Date.now() + this.startDelayMs;
    this.room.phase = 'countdown';
    this.emit('game:start', {
      trackId: settings.trackId,
      laps: settings.laps,
      totalCars,
      difficulty: settings.difficulty,
      startPositions,
      startAt: this.startAt,
    });

    this.timers.push(setTimeout(() => {
      if (!this.ended) this.room.phase = 'racing';
    }, this.startDelayMs));
    this.loop = setInterval(() => this.broadcast(), this.tickMs);
  }

  broadcast() {
    const players = [];
    for (const [id, s] of this.states) {
      if (s.hasPosition) players.push(compactState(id, s));
    }
    this.emit('game:update', { players, serverTime: Date.now() });
  }

  validateAndApply(id, data) {
    const s = this.states.get(id);
    if (!s || !data || typeof data !== 'object') return { ok: false, reason: 'invalid' };
    const { x, y, z, rotation, speed } = data;
    if (![x, y, z, rotation, speed].every(isFiniteNumber)) return { ok: false, reason: 'invalid' };

    const lap = Number.isInteger(data.lap) ? data.lap : s.lap;
    const checkpoint = Number.isInteger(data.checkpoint) ? data.checkpoint : s.checkpoint;

    if (s.hasPosition && !data.reset && distance(s, { x, y, z }) > MAX_POSITION_JUMP) {
      return { ok: false, reason: 'jump' };
    }
    const sameLap = lap === s.lap && (checkpoint === s.checkpoint || checkpoint === s.checkpoint + 1);
    const nextLap = lap === s.lap + 1 && checkpoint === 0;
    if (!sameLap && !nextLap) return { ok: false, reason: 'checkpoint' };

    Object.assign(s, { x, y, z, rotation, speed, lap, checkpoint, hasPosition: true });
    return { ok: true };
  }

  onPosition(playerId, data) {
    const result = this.validateAndApply(playerId, data);
    if (!result.ok) {
      const s = this.states.get(playerId);
      return s && s.hasPosition
        ? { rejected: true, reason: result.reason, correction: { x: s.x, y: s.y, z: s.z, rotation: s.rotation, lap: s.lap, checkpoint: s.checkpoint } }
        : { rejected: true, reason: result.reason, correction: null };
    }
    return { rejected: false };
  }

  onAiPositions(list) {
    if (!Array.isArray(list)) return;
    for (const entry of list.slice(0, 16)) {
      if (!entry || typeof entry.id !== 'string') continue;
      const s = this.states.get(entry.id);
      if (!s || !s.isAI) continue;
      this.validateAndApply(entry.id, { ...entry, reset: true });
      if (!s.finished && isFiniteNumber(entry.finishedTime)) {
        this.registerFinish(entry.id, entry.finishedTime, isFiniteNumber(entry.bestLap) ? entry.bestLap : null);
      }
    }
  }

  onFinished(playerId, data) {
    const s = this.states.get(playerId);
    if (!s || s.finished || this.ended) return;
    const totalTime = data && isFiniteNumber(data.totalTime) ? data.totalTime : null;
    const bestLap = data && isFiniteNumber(data.bestLap) ? data.bestLap : null;
    this.registerFinish(playerId, totalTime, bestLap);
  }

  registerFinish(id, totalTime, bestLap) {
    const s = this.states.get(id);
    s.finished = true;
    s.totalTime = totalTime;
    s.bestLap = bestLap;
    this.finishOrder.push(id);

    if (!s.isAI) {
      const humanFinishes = this.finishOrder.filter((fid) => !this.states.get(fid).isAI).length;
      if (humanFinishes === 1) {
        this.timers.push(setTimeout(() => this.end(), this.finishGraceMs));
      }
    }
    this.checkAllHumansFinished();
  }

  checkAllHumansFinished() {
    const connectedHumans = [...this.room.players.values()].filter((p) => p.connected);
    if (connectedHumans.length > 0 && connectedHumans.every((p) => this.states.get(p.id)?.finished)) {
      this.end();
    }
  }

  removePlayer(playerId) {
    this.states.delete(playerId);
    if (!this.ended) this.checkAllHumansFinished();
  }

  buildRankings() {
    const nameOf = (id) => this.room.players.get(id)?.name ?? null;
    const finished = this.finishOrder.filter((id) => this.states.has(id));
    const rest = [...this.states.keys()]
      .filter((id) => !finished.includes(id))
      .sort((a, b) => {
        const sa = this.states.get(a);
        const sb = this.states.get(b);
        return sb.lap - sa.lap || sb.checkpoint - sa.checkpoint;
      });
    return [...finished, ...rest].map((id, i) => {
      const s = this.states.get(id);
      return {
        position: i + 1,
        id,
        isAI: s.isAI,
        name: nameOf(id),
        finished: s.finished,
        totalTime: s.totalTime ?? null,
        bestLap: s.bestLap ?? null,
        lap: s.lap,
      };
    });
  }

  end() {
    if (this.ended) return;
    this.ended = true;
    this.stopTimers();
    this.room.phase = 'finished';
    this.emit('game:finished', { rankings: this.buildRankings() });
  }

  stopTimers() {
    if (this.loop) clearInterval(this.loop);
    this.loop = null;
    for (const t of this.timers) clearTimeout(t);
    this.timers = [];
  }

  dispose() {
    this.ended = true;
    this.stopTimers();
  }
}

module.exports = GameSession;
