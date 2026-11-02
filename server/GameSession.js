const crypto = require('crypto');
const { isFiniteNumber, compactState, shuffle } = require('./utils');

const TICK_MS = 50;
const START_DELAY_MS = 4000;
const FINISH_GRACE_MS = 30000;
// LARGADA SINCRONIZADA: o servidor espera TODOS terminarem de carregar a pista (player:loaded) e só então manda o
// "vai" (game:go) com o MESMO tempo de luzes para todos. Antes cada aparelho largava quando acabava de carregar (quem
// carregava antes largava antes; a IA, pilotada pelo anfitrião, só saía quando ele chegava) e o tempo aleatório com as
// 5 luzes acesas era sorteado em cada aparelho.
const LOAD_TIMEOUT_MS = 240000;   // aparelho travado no carregamento não segura a sala para sempre
const GO_LEAD_MS = 1500;          // folga para o "vai" chegar a todos antes das luzes
const LIGHTS_MS = 8000;           // 0,5 s + 1 s + 5 luzes — só para a fase "racing" do servidor

class GameSession {
  constructor(room, emit, options = {}) {
    this.room = room;
    this.emit = emit;
    this.tickMs = options.tickMs ?? TICK_MS;
    this.startDelayMs = options.startDelayMs ?? START_DELAY_MS;
    this.finishGraceMs = options.finishGraceMs ?? FINISH_GRACE_MS;
    this.loadTimeoutMs = options.loadTimeoutMs ?? LOAD_TIMEOUT_MS;
    this.goLeadMs = options.goLeadMs ?? GO_LEAD_MS;
    this.round = options.round ?? 1;
    this.states = new Map();
    this.timers = [];
    this.loop = null;
    this.startAt = 0;
    this.ended = false;
    this.loaded = new Set();
    this.goSent = false;
    this.holdMs = 0;
    this.finishSeq = 0;
    this.firstHumanFinished = false;
    this.startPositions = [];
    // Ritmo de cada jogador nesta pista (perfil "v;v;..." mandado ao carregar): se ele cair, o anfitrião pilota o carro
    // dele NESSE ritmo (a IA não fica mais rápida do que ele).
    this.pace = new Map();
    this.onEnd = options.onEnd ?? null;
  }

  /** prev = grid da corrida anterior (reinício votado: as MESMAS vagas). Sem prev: grid sorteado. */
  start(prev = null) {
    const humans = [...this.room.players.values()];
    const settings = this.room.settings;
    const totalCars = Math.max(settings.totalCars, humans.length);
    let startPositions = null;
    if (Array.isArray(prev) && prev.length === totalCars && humans.every((p) => prev.some((sp) => sp.id === p.id))) {
      startPositions = prev.map((sp) => {
        if (sp.isAI) return { ...sp };
        const p = this.room.players.get(sp.id);
        // Quem saiu da sala vira IA na mesma vaga (o grid continua completo).
        return p ? { ...sp, name: p.name, carModel: p.carModel, carColor: p.carColor } : { id: `ai_r${sp.slot}`, slot: sp.slot, isAI: true };
      });
    } else {
      const slots = shuffle([...Array(totalCars).keys()]);
      startPositions = [];
      humans.forEach((p, i) => {
        startPositions.push({ id: p.id, slot: slots[i], isAI: false, name: p.name, carModel: p.carModel, carColor: p.carColor });
      });
      for (let i = humans.length; i < totalCars; i++) {
        startPositions.push({ id: `ai_${i - humans.length}`, slot: slots[i], isAI: true });
      }
      startPositions.sort((a, b) => a.slot - b.slot);
    }
    this.startPositions = startPositions;

    for (const sp of startPositions) {
      this.states.set(sp.id, {
        isAI: sp.isAI, name: sp.isAI ? null : sp.name,
        x: 0, y: 0, z: 0, rotation: 0, speed: 0, lap: 1, checkpoint: 0, dist: 0,
        hasPosition: false, finished: false, retired: false,
      });
    }

    // startAt continua no pacote para clientes antigos; os novos (sync) esperam o game:go.
    this.startAt = Date.now() + this.startDelayMs;
    this.room.phase = 'loading';
    this.emit('game:start', {
      trackId: settings.trackId,
      laps: settings.laps,
      totalCars,
      difficulty: settings.difficulty,
      category: settings.category || 'gt',
      pitStops: !!settings.pitStops,
      weather: settings.weather || '',
      totalLoss: settings.totalLoss !== false,
      penalties: settings.penalties !== false,
      startPositions,
      startAt: this.startAt,
      sync: true,
      round: this.round,
    });
    this.timers.push(setTimeout(() => this.go(), this.loadTimeoutMs));
    this.loop = setInterval(() => this.broadcast(), this.tickMs);
    this.emitWaiting();
  }

  connectedPlayers() {
    return [...this.room.players.values()].filter((p) => p.connected);
  }

  waitingNames() {
    return this.connectedPlayers().filter((p) => !this.loaded.has(p.id)).map((p) => p.name);
  }

  emitWaiting() {
    if (this.goSent || this.ended) return;
    this.emit('game:waiting', { waiting: this.waitingNames(), round: this.round });
  }

  /** Aparelho terminou de montar a pista. late = a largada já foi dada (ele entra direto). */
  onLoaded(playerId, pace) {
    if (this.ended) return { late: false };
    if (typeof pace === 'string' && pace.length <= 1500 && /^[0-9.;]*$/.test(pace)) this.pace.set(playerId, pace);
    this.loaded.add(playerId);
    if (this.goSent) return { late: true };
    this.emitWaiting();
    this.checkAllLoaded();
    return { late: false };
  }

  /** Alguém caiu/voltou: a espera da largada considera só quem está conectado. */
  onConnectionChange() {
    if (this.ended) return;
    if (!this.goSent) {
      this.emitWaiting();
      this.checkAllLoaded();
    } else {
      this.checkAllHumansFinished();
    }
  }

  checkAllLoaded() {
    const connected = this.connectedPlayers();
    if (connected.length > 0 && connected.every((p) => this.loaded.has(p.id))) this.go();
  }

  go() {
    if (this.goSent || this.ended) return;
    this.goSent = true;
    this.holdMs = 200 + crypto.randomInt(2801);   // F1: 0,2–3 s com as 5 luzes acesas — o MESMO para todos
    this.room.phase = 'countdown';
    this.emit('game:go', { inMs: this.goLeadMs, holdMs: this.holdMs, round: this.round });
    this.timers.push(setTimeout(() => {
      if (!this.ended) this.room.phase = 'racing';
    }, this.goLeadMs + LIGHTS_MS + this.holdMs));
  }

  broadcast() {
    const players = [];
    for (const [id, s] of this.states) {
      if (s.hasPosition) players.push(compactState(id, s));
    }
    this.emit('game:update', { players, serverTime: Date.now() });
  }

  /** Posição de um carro. Sem a antiga trava de "salto"/checkpoint: depois de um respawn, do VOLTAR À PISTA ou de uma
   * reposição do pit o servidor recusava TODAS as posições seguintes e o carro ficava congelado na tela dos outros. */
  validateAndApply(id, data) {
    const s = this.states.get(id);
    if (!s || !data || typeof data !== 'object') return { ok: false, reason: 'invalid' };
    const { x, y, z, rotation, speed } = data;
    if (![x, y, z, rotation, speed].every(isFiniteNumber)) return { ok: false, reason: 'invalid' };
    const lap = Number.isInteger(data.lap) && data.lap >= 1 && data.lap <= 1000 ? data.lap : s.lap;
    const checkpoint = Number.isInteger(data.checkpoint) && data.checkpoint >= 0 ? data.checkpoint : s.checkpoint;
    Object.assign(s, { x, y, z, rotation, speed, lap, checkpoint, hasPosition: true });
    if (isFiniteNumber(data.dist)) s.dist = data.dist;
    if (data.st === 2 && !s.finished) s.retired = true;
    return { ok: true };
  }

  onPosition(playerId, data) {
    const result = this.validateAndApply(playerId, data);
    if (!result.ok) return { rejected: true, reason: result.reason, correction: null };
    return { rejected: false };
  }

  /** Jogador caiu no meio da corrida: o carro dele segue no automático (pilotado pelo anfitrião, no ritmo dele). */
  onDropped(playerId) {
    const s = this.states.get(playerId);
    if (!s || s.isAI || this.ended || s.finished || s.retired) return;
    s.dropped = true;
    this.emit('player:dropped', { playerId, name: s.name, pace: this.pace.get(playerId) || '' });
  }

  /** Voltou: o carro volta a ser dirigido por ele. */
  onBack(playerId) {
    const s = this.states.get(playerId);
    if (!s || !s.dropped) return;
    s.dropped = false;
    if (!this.ended) this.emit('player:back', { playerId });
  }

  /** Posições que o ANFITRIÃO transmite: a IA e o carro de quem caiu. */
  onAiPositions(list) {
    if (!Array.isArray(list)) return;
    for (const entry of list.slice(0, 24)) {
      if (!entry || typeof entry.id !== 'string') continue;
      const s = this.states.get(entry.id);
      if (!s || (!s.isAI && !s.dropped)) continue;
      this.validateAndApply(entry.id, entry);
      if (!s.finished && isFiniteNumber(entry.finishedTime) && entry.finishedTime > 0) {
        this.registerFinish(entry.id, entry.finishedTime,
          isFiniteNumber(entry.bestLap) ? entry.bestLap : null,
          isFiniteNumber(entry.penalty) ? entry.penalty : 0);
      }
    }
  }

  onFinished(playerId, data) {
    const s = this.states.get(playerId);
    if (!s || s.finished || this.ended) return;
    // Abandono do jogador (perda total, quebra, desclassificado): conta como "terminou" para fechar a corrida.
    if (data && data.retired === true) {
      s.retired = true;
      this.checkAllHumansFinished();
      return;
    }
    const totalTime = data && isFiniteNumber(data.totalTime) ? data.totalTime : null;
    const bestLap = data && isFiniteNumber(data.bestLap) ? data.bestLap : null;
    const penalty = data && isFiniteNumber(data.penalty) ? data.penalty : 0;
    this.registerFinish(playerId, totalTime, bestLap, penalty);
  }

  registerFinish(id, totalTime, bestLap, penalty = 0) {
    const s = this.states.get(id);
    if (!s || s.finished) return;
    s.finished = true;
    s.retired = false;
    s.totalTime = totalTime;
    s.bestLap = bestLap;
    s.penalty = penalty;
    s.finishSeq = ++this.finishSeq;

    if (!s.isAI && !this.firstHumanFinished) {
      this.firstHumanFinished = true;
      this.timers.push(setTimeout(() => this.end(), this.finishGraceMs));
    }
    this.checkAllHumansFinished();
  }

  checkAllHumansFinished() {
    if (this.ended) return;
    const connectedHumans = this.connectedPlayers();
    if (connectedHumans.length > 0 && connectedHumans.every((p) => {
      const s = this.states.get(p.id);
      return s && (s.finished || s.retired);
    })) {
      this.end();
    }
  }

  removePlayer(playerId) {
    // Quem saiu no meio continua no placar como abandono (antes sumia da tabela).
    const s = this.states.get(playerId);
    if (s && !s.isAI && !s.finished) s.retired = true;
    this.loaded.delete(playerId);
    if (!this.ended) this.onConnectionChange();
  }

  /** Placar OFICIAL (igual para todos): quem terminou pelo TEMPO FINAL (já com penalidades — como na F1, a penalidade
   * pode tirar posição); depois quem ainda corria pela distância percorrida; abandonos no fim. Antes cada aparelho
   * montava o próprio placar e no convidado ele aparecia em 1º tendo chegado em 6º. */
  buildRankings() {
    const timeOf = (s) => (isFiniteNumber(s.totalTime) && s.totalTime > 0 ? s.totalTime : Number.POSITIVE_INFINITY);
    const list = [...this.states.entries()].map(([id, s]) => ({ id, s }));
    list.sort((a, b) => {
      if (a.s.finished !== b.s.finished) return a.s.finished ? -1 : 1;
      if (a.s.finished) {
        const ta = timeOf(a.s);
        const tb = timeOf(b.s);
        if (ta !== tb) return ta < tb ? -1 : 1;
        return a.s.finishSeq - b.s.finishSeq;
      }
      if (a.s.retired !== b.s.retired) return a.s.retired ? 1 : -1;
      return (b.s.lap - a.s.lap) || ((b.s.dist || 0) - (a.s.dist || 0)) || (b.s.checkpoint - a.s.checkpoint);
    });
    const nameOf = (id, s) => this.room.players.get(id)?.name ?? s.name ?? null;
    return list.map(({ id, s }, i) => ({
      position: i + 1,
      id,
      isAI: s.isAI,
      name: nameOf(id, s),
      finished: s.finished,
      retired: !!s.retired,
      totalTime: s.totalTime ?? null,
      bestLap: s.bestLap ?? null,
      penalty: s.penalty ?? 0,
      lap: s.lap,
    }));
  }

  end() {
    if (this.ended) return;
    this.ended = true;
    this.stopTimers();
    this.room.phase = 'finished';
    this.emit('game:finished', { rankings: this.buildRankings(), round: this.round });
    if (this.onEnd) this.onEnd();
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
