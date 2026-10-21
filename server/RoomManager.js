const GameSession = require('./GameSession');
const {
  MAX_HUMANS,
  DEFAULT_SETTINGS,
  generateRoomCode,
  generateId,
  normalizeRoomCode,
  sanitizeName,
  sanitizeSettings,
  sanitizeCar,
  isFiniteNumber,
} = require('./utils');

const IDLE_ROOM_MS = 30 * 60 * 1000;
const RECONNECT_GRACE_MS = 15000;

class RoomManager {
  constructor(io, options = {}) {
    this.io = io;
    this.maxRooms = options.maxRooms ?? 100;
    this.minPlayers = options.minPlayers ?? 2;
    this.idleRoomMs = options.idleRoomMs ?? IDLE_ROOM_MS;
    this.reconnectGraceMs = options.reconnectGraceMs ?? RECONNECT_GRACE_MS;
    this.sessionOptions = options.sessionOptions ?? {};
    this.rooms = new Map();
    this.sockets = new Map();
    this.cleanupTimer = setInterval(() => this.cleanupIdle(), options.cleanupIntervalMs ?? 60000);
    this.cleanupTimer.unref?.();
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
  }

  updatePlayer(socket, data = {}) {
    const ctx = this.context(socket);
    if (!ctx) return;
    const { room, player } = ctx;
    if (room.phase !== 'lobby') return;
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
    room.settings = sanitizeSettings(data, room.settings);
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
    if (!players.every((p) => p.ready)) return this.error(socket, 'Nem todos estão prontos');

    room.session = new GameSession(room, (event, payload) => this.io.to(room.code).emit(event, payload), this.sessionOptions);
    room.session.start();
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
      player.connected = false;
      player.disconnectTimer = setTimeout(() => this.removePlayer(room, player), this.reconnectGraceMs);
      this.reassignHostIfNeeded(room, player);
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
    if (room.phase === 'lobby' || room.phase === 'finished') this.broadcastRoom(room);
  }

  deleteRoom(room, reason) {
    if (room.session) room.session.dispose();
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
