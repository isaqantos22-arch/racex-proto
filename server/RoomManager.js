const GameSession = require('./GameSession');
const {
  MAX_HUMANS,
  DEFAULT_SETTINGS,
  generateRoomCode,
  generateId,
  normalizeRoomCode,
  sanitizeName,
  sanitizeSettingsDetailed,
  sanitizeCar,
  isFiniteNumber,
} = require('./utils');

const IDLE_ROOM_MS = 30 * 60 * 1000;
const RECONNECT_GRACE_MS = 15000;
// Votação (reiniciar a corrida / jogar novamente): 10 s; quem não responde RECUSA.
const VOTE_MS = 10000;

class RoomManager {
  constructor(io, options = {}) {
    this.io = io;
    this.maxRooms = options.maxRooms ?? 100;
    this.minPlayers = options.minPlayers ?? 2;
    this.idleRoomMs = options.idleRoomMs ?? IDLE_ROOM_MS;
    this.reconnectGraceMs = options.reconnectGraceMs ?? RECONNECT_GRACE_MS;
    this.voteMs = options.voteMs ?? VOTE_MS;
    this.sessionOptions = options.sessionOptions ?? {};
    this.rooms = new Map();
    this.sockets = new Map();
    this.cleanupTimer = setInterval(() => this.cleanupIdle(), options.cleanupIntervalMs ?? 60000);
    this.cleanupTimer.unref?.();
    // Eventos novos (largada sincronizada e votações) registrados aqui: o index.js continua igual.
    io.on('connection', (socket) => {
      const on = (event, handler) => socket.on(event, (...args) => {
        try {
          handler(...args);
        } catch (err) {
          console.error(`[${event}]`, err);
        }
      });
      on('player:loaded', (d) => this.onLoaded(socket, d));
      on('vote:request', (d) => this.voteRequest(socket, d));
      on('vote:answer', (d) => this.voteAnswer(socket, d));
    });
  }

  count() {
    return this.rooms.size;
  }

  error(socket, message) {
    socket.emit('room:error', { message });
  }

  context(socket) {
    const ref = this.sockets.get(socket.id);
    if (!ref) return null;
    const room = this.rooms.get(ref.roomCode);
    if (!room) return null;
    const player = room.players.get(ref.playerId);
    if (!player) return null;
    room.lastActivity = Date.now();
    return { room, player };
  }

  publicRoom(room) {
    return {
      roomCode: room.code,
      phase: room.phase,
      hostId: room.hostId,
      settings: room.settings,
      players: [...room.players.values()].map((p) => ({
        id: p.id,
        name: p.name,
        carModel: p.carModel,
        carColor: p.carColor,
        ready: p.ready,
        connected: p.connected,
        isHost: p.id === room.hostId,
      })),
    };
  }

  broadcastRoom(room, joiner = null) {
    const payload = this.publicRoom(room);
    if (joiner) {
      this.io.to(room.code).except(joiner.socket.id).emit('room:joined', payload);
      joiner.socket.emit('room:joined', { ...payload, playerId: joiner.player.id, token: joiner.player.token });
    } else {
      this.io.to(room.code).emit('room:joined', payload);
    }
  }

  addPlayer(socket, room, name) {
    const player = {
      id: generateId('p'),
      token: generateId('t'),
      socketId: socket.id,
      name: sanitizeName(name),
      carModel: 'titan',
      carColor: 0,
      ready: false,
      connected: true,
      joinedAt: Date.now() + room.players.size / 1000,
      disconnectTimer: null,
      lastInput: null,
    };
    room.players.set(player.id, player);
    this.sockets.set(socket.id, { roomCode: room.code, playerId: player.id });
    socket.join(room.code);
    return player;
  }

  create(socket, data = {}) {
    if (this.sockets.has(socket.id)) return this.error(socket, 'Você já está em uma sala');
    if (this.rooms.size >= this.maxRooms) return this.error(socket, 'Servidor cheio');

    const code = generateRoomCode((c) => this.rooms.has(c));
    const room = {
      code,
      hostId: null,
      phase: 'lobby',
      settings: { ...DEFAULT_SETTINGS },
      players: new Map(),
      session: null,
      vote: null,
      round: 0,
      lastActivity: Date.now(),
    };
    this.rooms.set(code, room);
    const player = this.addPlayer(socket, room, data && data.hostName);
    room.hostId = player.id;
    socket.emit('room:created', { roomCode: code });
    this.broadcastRoom(room, { socket, player });
  }

  join(socket, data = {}) {
    if (this.sockets.has(socket.id)) return this.error(socket, 'Você já está em uma sala');
    const code = normalizeRoomCode(data && data.roomCode);
    const room = code && this.rooms.get(code);
    if (!room) return this.error(socket, 'Código de sala inválido');
    if (room.phase !== 'lobby') return this.error(socket, 'Corrida em andamento');
    if (room.players.size >= MAX_HUMANS) return socket.emit('room:full');

    room.lastActivity = Date.now();
    const player = this.addPlayer(socket, room, data.playerName);
    this.broadcastRoom(room, { socket, player });
  }

  rejoin(socket, data = {}) {
    const code = normalizeRoomCode(data && data.roomCode);
    const room = code && this.rooms.get(code);
    if (!room) return this.error(socket, 'Sala não encontrada');
    const player = [...room.players.values()].find((p) => p.token === data.token);
    if (!player) return this.error(socket, 'Sessão expirada');

    if (player.disconnectTimer) clearTimeout(player.disconnectTimer);
    player.disconnectTimer = null;
    this.sockets.delete(player.socketId);
    player.socketId = socket.id;
    player.connected = true;
    this.sockets.set(socket.id, { roomCode: room.code, playerId: player.id });
    socket.join(room.code);
    room.lastActivity = Date.now();
    this.broadcastRoom(room, { socket, player });
    if (room.session) {
      room.session.onBack(player.id);   // volta a dirigir o próprio carro
      room.session.onConnectionChange();
    }
  }

  updatePlayer(socket, data = {}) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    if (room.phase !== 'lobby' && room.phase !== 'finished') return;
    if (data && data.playerName !== undefined) player.name = sanitizeName(data.playerName, player.name);
    Object.assign(player, sanitizeCar(data, { carModel: player.carModel, carColor: player.carColor }));
    this.broadcastRoom(room);
  }

  setReady(socket, data) {
    const ctx = this.context(socket);
    if (!ctx || ctx.room.phase !== 'lobby') return;
    ctx.player.ready = data && typeof data.ready === 'boolean' ? data.ready : !ctx.player.ready;
    this.broadcastRoom(ctx.room);
  }

  updateSettings(socket, data) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    if (player.id !== room.hostId) return this.error(socket, 'Somente o host altera as configurações');
    if (room.phase !== 'lobby') return;
    const { settings, rejected } = sanitizeSettingsDetailed(data, room.settings);
    room.settings = settings;
    if (rejected.length > 0) socket.emit('room:warning', { message: `Servidor recusou: ${rejected.join(', ')}` });
    this.broadcastRoom(room);
  }

  start(socket) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    if (player.id !== room.hostId) return this.error(socket, 'Somente o host inicia a corrida');
    if (room.phase !== 'lobby') return this.error(socket, 'Corrida em andamento');
    const players = [...room.players.values()];
    if (players.length < this.minPlayers) return this.error(socket, `São necessários pelo menos ${this.minPlayers} jogadores`);
    // O anfitrião inicia: começar já é o "pronto" dele — só os CONVIDADOS precisam marcar pronto.
    player.ready = true;
    if (!players.every((p) => p.ready)) return this.error(socket, 'Nem todos estão prontos');
    this.launchSession(room, null);
  }

  /** Nova corrida NA MESMA SALA (início, reinício votado ou nova partida votada). prev = grid a repetir. */
  launchSession(room, prev) {
    if (room.session) room.session.dispose();
    // Quem caiu e não voltou não entra na corrida nova.
    for (const p of [...room.players.values()]) if (!p.connected) this.removePlayer(room, p);
    if (!this.rooms.has(room.code)) return;
    room.round = (room.round || 0) + 1;
    for (const p of room.players.values()) p.ready = true;
    room.session = new GameSession(room, (event, payload) => this.io.to(room.code).emit(event, payload), {
      ...this.sessionOptions,
      round: room.round,
      onEnd: () => this.cleanupDisconnected(room),
    });
    room.session.start(prev);
  }

  /** Fim da corrida: quem caiu e não voltou sai da sala agora (durante a corrida o carro dele seguiu no automático). */
  cleanupDisconnected(room) {
    setImmediate(() => {
      if (!this.rooms.has(room.code)) return;
      for (const p of [...room.players.values()]) if (!p.connected) this.removePlayer(room, p);
    });
  }

  returnToLobby(socket) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    if (player.id !== room.hostId || room.phase !== 'finished') return;
    if (room.session) room.session.dispose();
    room.session = null;
    room.phase = 'lobby';
    for (const p of room.players.values()) p.ready = false;
    this.broadcastRoom(room);
  }

  onInput(socket, data) {
    const ctx = this.context(socket);
    if (!ctx || !data) return;
    const { steering, throttle, brake } = data;
    if ([steering, throttle, brake].every(isFiniteNumber)) {
      ctx.player.lastInput = { steering, throttle, brake };
    }
  }

  onPosition(socket, data) {
    const ctx = this.context(socket);
    if (!ctx || !ctx.room.session || ctx.room.session.ended) return;
    const result = ctx.room.session.onPosition(ctx.player.id, data);
    if (result.rejected) socket.emit('player:correction', { reason: result.reason, state: result.correction });
  }

  onAiPositions(socket, data) {
    const ctx = this.context(socket);
    if (!ctx || !ctx.room.session || ctx.player.id !== ctx.room.hostId) return;
    ctx.room.session.onAiPositions(data);
  }

  onFinished(socket, data) {
    const ctx = this.context(socket);
    if (!ctx || !ctx.room.session) return;
    ctx.room.session.onFinished(ctx.player.id, data);
  }

  /** Aparelho terminou de carregar a pista; quem chega depois da largada recebe o "vai" na hora. */
  onLoaded(socket, data) {
    const ctx = this.context(socket);
    if (!ctx || !ctx.room.session) return;
    const { late } = ctx.room.session.onLoaded(ctx.player.id, data && data.pace);
    if (late) socket.emit('game:go', { inMs: 0, holdMs: ctx.room.session.holdMs, round: ctx.room.session.round, late: true });
  }

  // ---------------- Votações ----------------
  /** REINICIAR (durante a corrida): todos precisam aceitar. JOGAR NOVAMENTE (depois do resultado): segue com quem
   * aceitar (mínimo 2) na MESMA sala — antes o botão recarregava a pista só no próprio aparelho e cada um ia correr
   * sozinho com os nomes dos outros na tela. */
  voteRequest(socket, data) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    const kind = data && (data.kind === 'restart' || data.kind === 'rematch') ? data.kind : null;
    if (!kind) return;
    if (room.vote) return this.error(socket, 'Já existe uma votação em andamento');
    if (kind === 'restart' && !(room.session && !room.session.ended)) return this.error(socket, 'Não há corrida em andamento');
    if (kind === 'rematch' && room.phase !== 'finished') return this.error(socket, 'Aguarde o resultado oficial da corrida');
    const eligible = [...room.players.values()].filter((p) => p.connected).map((p) => p.id);
    const vote = {
      id: generateId('v'),
      kind,
      requesterId: player.id,
      eligible: new Set(eligible),
      answers: new Map([[player.id, true]]),
      timer: null,
    };
    room.vote = vote;
    vote.timer = setTimeout(() => this.resolveVote(room, vote), this.voteMs + 500);
    this.io.to(room.code).emit('vote:start', {
      voteId: vote.id, kind, byId: player.id, byName: player.name, seconds: Math.round(this.voteMs / 1000),
    });
    this.maybeResolve(room, vote);
  }

  voteAnswer(socket, data) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    const vote = room.vote;
    if (!vote || !data || data.voteId !== vote.id || !vote.eligible.has(player.id) || vote.answers.has(player.id)) return;
    vote.answers.set(player.id, data.yes === true);
    const yes = [...vote.answers.values()].filter(Boolean).length;
    this.io.to(room.code).emit('vote:update', { voteId: vote.id, yes, no: vote.answers.size - yes, total: vote.eligible.size });
    this.maybeResolve(room, vote);
  }

  maybeResolve(room, vote) {
    if (room.vote !== vote) return;
    const active = [...vote.eligible].filter((id) => room.players.get(id)?.connected);
    // Reinício: um "não" já decide. Todos responderam (ou só sobrou quem pediu): decide agora.
    if (vote.kind === 'restart' && [...vote.answers.values()].some((v) => v === false)) return this.resolveVote(room, vote);
    if (active.every((id) => vote.answers.has(id))) this.resolveVote(room, vote);
  }

  resolveVote(room, vote) {
    if (room.vote !== vote) return;
    room.vote = null;
    clearTimeout(vote.timer);
    if (!this.rooms.has(room.code)) return;
    const ids = [...vote.eligible].filter((id) => room.players.get(id)?.connected);
    const yesIds = ids.filter((id) => vote.answers.get(id) === true);
    const noIds = ids.filter((id) => vote.answers.get(id) !== true);   // sem resposta = recusou
    const nameOf = (id) => room.players.get(id)?.name ?? '?';
    const accepted = vote.kind === 'restart'
      ? yesIds.length > 0 && noIds.length === 0
      : yesIds.length >= Math.max(2, this.minPlayers);
    this.io.to(room.code).emit('vote:result', {
      voteId: vote.id, kind: vote.kind, accepted, yes: yesIds.map(nameOf), no: noIds.map(nameOf),
    });
    if (!accepted) return;
    if (vote.kind === 'rematch') {
      // Quem recusou a nova partida sai da sala (volta ao menu); quem aceitou continua JUNTO.
      for (const id of noIds) this.kick(room, id, 'Você não entrou na nova partida');
      if (!this.rooms.has(room.code) || room.players.size < 2) return;
      this.launchSession(room, null);
    } else {
      this.launchSession(room, room.session ? room.session.startPositions : null);
    }
  }

  kick(room, playerId, message) {
    const player = room.players.get(playerId);
    if (!player) return;
    const sock = this.io.sockets.sockets.get(player.socketId);
    if (sock) {
      sock.emit('room:kicked', { message });
      sock.leave(room.code);
    }
    this.removePlayer(room, player);
  }

  leave(socket) {
    const ref = this.sockets.get(socket.id);
    if (!ref) return;
    this.sockets.delete(socket.id);
    const room = this.rooms.get(ref.roomCode);
    if (!room) return;
    const player = room.players.get(ref.playerId);
    if (!player || player.socketId !== socket.id) return;

    if (room.phase === 'lobby' || room.phase === 'finished') {
      this.removePlayer(room, player);
    } else {
      // Caiu NO MEIO DA CORRIDA: continua na corrida com o carro no automático (o anfitrião pilota, no ritmo dele) e pode
      // voltar até o fim (room:rejoin com a senha). Só sai de vez se a sala ficar sem ninguém conectado.
      player.connected = false;
      this.reassignHostIfNeeded(room, player);
      if (room.session) {
        room.session.onDropped(player.id);
        room.session.onConnectionChange();
      }
      if (room.vote) this.maybeResolve(room, room.vote);
      player.disconnectTimer = setTimeout(() => {
        player.disconnectTimer = null;
        if (player.connected || !room.players.has(player.id)) return;
        const anyone = [...room.players.values()].some((p) => p.connected);
        if (!anyone || !room.session || room.session.ended) this.removePlayer(room, player);
      }, this.reconnectGraceMs);
    }
  }

  reassignHostIfNeeded(room, leaving) {
    if (room.hostId !== leaving.id) return;
    const candidates = [...room.players.values()]
      .filter((p) => p.id !== leaving.id && p.connected)
      .sort((a, b) => a.joinedAt - b.joinedAt);
    if (candidates.length === 0) return;
    room.hostId = candidates[0].id;
    this.io.to(room.code).emit('room:host', { hostId: room.hostId });
  }

  removePlayer(room, player) {
    if (player.disconnectTimer) clearTimeout(player.disconnectTimer);
    this.reassignHostIfNeeded(room, player);
    room.players.delete(player.id);
    this.sockets.delete(player.socketId);
    if (room.session) room.session.removePlayer(player.id);

    if (room.players.size === 0 || ![...room.players.values()].some((p) => p.connected)) {
      this.deleteRoom(room);
      return;
    }
    this.io.to(room.code).emit('player:disconnected', { playerId: player.id });
    if (room.vote) this.maybeResolve(room, room.vote);
    if (room.phase === 'lobby' || room.phase === 'finished') this.broadcastRoom(room);
  }

  deleteRoom(room, reason) {
    if (room.session) room.session.dispose();
    if (room.vote) clearTimeout(room.vote.timer);
    room.vote = null;
    for (const p of room.players.values()) {
      if (p.disconnectTimer) clearTimeout(p.disconnectTimer);
      this.sockets.delete(p.socketId);
    }
    if (reason) this.io.to(room.code).emit('room:error', { message: reason });
    this.io.in(room.code).socketsLeave(room.code);
    this.rooms.delete(room.code);
  }

  cleanupIdle(now = Date.now()) {
    for (const room of [...this.rooms.values()]) {
      if (now - room.lastActivity > this.idleRoomMs) this.deleteRoom(room, 'Sala expirada por inatividade');
    }
  }

  dispose() {
    clearInterval(this.cleanupTimer);
    for (const room of [...this.rooms.values()]) this.deleteRoom(room);
  }
}

module.exports = RoomManager;
