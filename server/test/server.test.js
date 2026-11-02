const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { io: ioClient } = require('socket.io-client');
const { createServer } = require('../index');
const { generateRoomCode, ROOM_CODE_ALPHABET } = require('../utils');

let srv;
let url;
const clients = [];

before(async () => {
  srv = createServer({
    maxRooms: 3,
    minPlayers: 2,
    reconnectGraceMs: 300,
    voteMs: 400,
    sessionOptions: { tickMs: 50, startDelayMs: 200, finishGraceMs: 500, loadTimeoutMs: 1500, goLeadMs: 50 },
  });
  await new Promise((r) => srv.server.listen(0, r));
  url = `http://localhost:${srv.server.address().port}`;
});

after(async () => {
  for (const c of clients) c.close();
  await srv.close();
});

function connect() {
  const c = ioClient(url, { transports: ['websocket'], forceNew: true, reconnection: false });
  clients.push(c);
  return new Promise((resolve) => c.on('connect', () => resolve(c)));
}

function once(client, event, predicate = () => true, timeout = 2000) {
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error(`timeout esperando ${event}`)), timeout);
    const handler = (data) => {
      if (!predicate(data)) return;
      clearTimeout(t);
      client.off(event, handler);
      resolve(data);
    };
    client.on(event, handler);
  });
}

async function createRoomWithTwo() {
  const host = await connect();
  const created = once(host, 'room:created');
  const hostJoined = once(host, 'room:joined', (d) => d.token);
  host.emit('room:create', { hostName: 'Ana' });
  const { roomCode } = await created;
  const hostInfo = await hostJoined;
  const guest = await connect();
  const guestJoined = once(guest, 'room:joined', (d) => d.token);
  guest.emit('room:join', { roomCode: roomCode.toLowerCase(), playerName: 'Bruno' });
  const guestInfo = await guestJoined;
  return { host, guest, roomCode, hostInfo, guestInfo };
}

async function startRace(host, guest) {
  const bothReady = once(host, 'room:joined', (d) => d.players.length === 2 && d.players.every((p) => p.ready));
  host.emit('player:ready', { ready: true });
  guest.emit('player:ready', { ready: true });
  await bothReady;
  const hostStart = once(host, 'game:start');
  const guestStart = once(guest, 'game:start');
  host.emit('room:start');
  return Promise.all([hostStart, guestStart]);
}

test('código de sala tem 6 caracteres sem ambíguos', () => {
  for (let i = 0; i < 200; i++) {
    const code = generateRoomCode();
    assert.equal(code.length, 6);
    for (const ch of code) assert.ok(ROOM_CODE_ALPHABET.includes(ch));
    assert.ok(!/[O0I1]/.test(code));
  }
});

test('GET /health responde ok', async () => {
  const res = await fetch(`${url}/health`);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(typeof body.rooms, 'number');
});

test('time:sync retorna horário do servidor', async () => {
  const c = await connect();
  const res = await c.timeout(1000).emitWithAck('time:sync', 123);
  assert.equal(res.client, 123);
  assert.ok(Math.abs(res.server - Date.now()) < 1000);
  c.close();
});

test('criar e entrar na sala, código inválido gera erro', async () => {
  const { host, guest, roomCode, hostInfo, guestInfo } = await createRoomWithTwo();
  assert.match(roomCode, /^[A-Z2-9]{6}$/);
  assert.equal(hostInfo.hostId, hostInfo.playerId);
  assert.equal(guestInfo.players.length, 2);

  const bad = await connect();
  const err = once(bad, 'room:error');
  bad.emit('room:join', { roomCode: 'ZZZZZZ', playerName: 'X' });
  assert.equal((await err).message, 'Código de sala inválido');
  host.close(); guest.close(); bad.close();
});

test('somente host altera configurações e valores são validados', async () => {
  const { host, guest } = await createRoomWithTwo();
  const err = once(guest, 'room:error');
  guest.emit('room:settings', { laps: 5 });
  assert.match((await err).message, /host/);

  const upd = once(guest, 'room:joined', (d) => d.settings.laps === 5);
  host.emit('room:settings', { trackId: 'city', laps: 5, totalCars: 99, difficulty: 'expert' });
  const d = await upd;
  assert.equal(d.settings.trackId, 'city');
  assert.equal(d.settings.totalCars, 8);
  assert.equal(d.settings.difficulty, 'expert');

  const carUpd = once(host, 'room:joined', (x) => x.players.some((p) => p.carModel === 'storm'));
  guest.emit('player:join', { carModel: 'storm', carColor: 3 });
  const c = await carUpd;
  assert.equal(c.players.find((p) => p.carModel === 'storm').carColor, 3);
  host.close(); guest.close();
});

test('iniciar exige todos prontos; corrida completa com ranking', async () => {
  const { host, guest, hostInfo, guestInfo } = await createRoomWithTwo();
  const err = once(host, 'room:error');
  host.emit('room:start');
  assert.equal((await err).message, 'Nem todos estão prontos');

  const [start] = await startRace(host, guest);
  assert.equal(start.startPositions.length, 8);
  assert.equal(start.startPositions.filter((p) => p.isAI).length, 6);
  assert.ok(start.startAt > Date.now());
  assert.equal(new Set(start.startPositions.map((p) => p.slot)).size, 8);

  const update = once(guest, 'game:update', (d) => d.players.some((p) => p.id === hostInfo.playerId));
  host.emit('player:position', { x: 1.2345, y: 0, z: 2, rotation: 90.6, speed: 10, lap: 1, checkpoint: 0, t: 1 });
  const u = await update;
  const hp = u.players.find((p) => p.id === hostInfo.playerId);
  assert.equal(hp.x, 1.23);
  assert.equal(hp.rotation, 91);

  // Respawn / VOLTAR À PISTA / reposição do pit: o salto é ACEITO (antes o carro ficava congelado na tela dos outros).
  const jumped = once(guest, 'game:update', (d) => d.players.some((p) => p.id === hostInfo.playerId && p.x === 100));
  host.emit('player:position', { x: 100, y: 0, z: 2, rotation: 0, speed: 10, lap: 1, checkpoint: 0 });
  await jumped;

  host.emit('ai:positions', [{ id: 'ai_0', x: 5, y: 0, z: 5, rotation: 0, speed: 20, lap: 1, finishedTime: 60 }]);
  await new Promise((r) => setTimeout(r, 50));
  const finished = once(host, 'game:finished');
  guest.emit('player:finished', { totalTime: 61, bestLap: 20 });
  host.emit('player:finished', { totalTime: 62, bestLap: 19 });
  const { rankings } = await finished;
  assert.equal(rankings.length, 8);
  assert.equal(rankings[0].id, 'ai_0');
  assert.equal(rankings[1].id, guestInfo.playerId);
  assert.equal(rankings[2].id, hostInfo.playerId);
  assert.equal(rankings[2].name, 'Ana');

  const back = once(guest, 'room:joined', (d) => d.phase === 'lobby');
  host.emit('room:return');
  const lobby = await back;
  assert.ok(lobby.players.every((p) => !p.ready));
  host.close(); guest.close();
});

test('entrar durante corrida é bloqueado e sala cheia com 8', async () => {
  const { host, guest, roomCode } = await createRoomWithTwo();
  await startRace(host, guest);
  const late = await connect();
  const err = once(late, 'room:error');
  late.emit('room:join', { roomCode, playerName: 'Tarde' });
  assert.equal((await err).message, 'Corrida em andamento');
  host.close(); guest.close(); late.close();

  const h = await connect();
  const created = once(h, 'room:created');
  h.emit('room:create', { hostName: 'H' });
  const { roomCode: code } = await created;
  const joiners = [];
  for (let i = 0; i < 7; i++) {
    const c = await connect();
    const j = once(c, 'room:joined', (d) => d.token);
    c.emit('room:join', { roomCode: code, playerName: `P${i}` });
    await j;
    joiners.push(c);
  }
  const ninth = await connect();
  const full = once(ninth, 'room:full');
  ninth.emit('room:join', { roomCode: code, playerName: 'Nono' });
  await full;
  h.close(); ninth.close(); joiners.forEach((c) => c.close());
});

test('host saindo na corrida passa host ao mais antigo; reconexão por token', async () => {
  const { host, guest, roomCode, guestInfo, hostInfo } = await createRoomWithTwo();
  await startRace(host, guest);
  const third = await connect();
  const hostChange = once(guest, 'room:host');
  const dropped = once(guest, 'player:dropped', (d) => d.playerId === hostInfo.playerId);
  host.close();
  assert.equal((await hostChange).hostId, guestInfo.playerId);
  // Caiu no meio da corrida: continua na corrida (carro no automático), não é removido.
  await dropped;

  const g2 = await connect();
  const rj = once(g2, 'room:joined', (d) => d.token);
  guest.close();
  await new Promise((r) => setTimeout(r, 50));
  g2.emit('room:rejoin', { roomCode, token: guestInfo.token });
  const info = await rj;
  assert.equal(info.playerId, guestInfo.playerId);
  g2.close(); third.close();
});

test('limite de salas retorna "Servidor cheio"', async () => {
  await new Promise((r) => setTimeout(r, 400));
  const hosts = [];
  const toCreate = srv.rooms.maxRooms - srv.rooms.count();
  for (let i = 0; i < toCreate; i++) {
    const c = await connect();
    const created = once(c, 'room:created');
    c.emit('room:create', { hostName: `H${i}` });
    await created;
    hosts.push(c);
  }
  const extra = await connect();
  const err = once(extra, 'room:error');
  extra.emit('room:create', { hostName: 'X' });
  assert.equal((await err).message, 'Servidor cheio');
  hosts.forEach((c) => c.close());
  extra.close();
});

test('salas paradas por mais de 30 min são removidas', async () => {
  await new Promise((r) => setTimeout(r, 200));
  const c = await connect();
  const created = once(c, 'room:created');
  c.emit('room:create', { hostName: 'Velho' });
  const { roomCode } = await created;
  const err = once(c, 'room:error');
  srv.rooms.cleanupIdle(Date.now() + 31 * 60 * 1000);
  assert.match((await err).message, /inatividade/);
  assert.equal(srv.rooms.rooms.has(roomCode), false);
  c.close();
});

test('queda na corrida: anfitrião pilota o carro de quem caiu no ritmo dele; ao voltar, retoma', async () => {
  const { host, guest, roomCode, guestInfo } = await createRoomWithTwo();
  await startRace(host, guest);
  const pace = Array(128).fill('55.0').join(';');
  guest.emit('player:loaded', { pace });
  host.emit('player:loaded', { pace: '' });
  await once(host, 'game:go');
  const dropped = once(host, 'player:dropped', (d) => d.playerId === guestInfo.playerId);
  guest.close();
  const d = await dropped;
  assert.equal(d.pace, pace);
  assert.equal(d.name, 'Bruno');
  // O anfitrião transmite o carro de quem caiu junto com a IA.
  const relayed = once(host, 'game:update', (u) => u.players.some((p) => p.id === guestInfo.playerId && p.x === 42));
  host.emit('ai:positions', [{ id: guestInfo.playerId, x: 42, y: 0, z: 1, rotation: 0, speed: 100, lap: 1 }]);
  await relayed;
  await new Promise((r) => setTimeout(r, 400));   // passou a carência de 300 ms: continua na sala
  assert.equal(srv.rooms.rooms.get(roomCode).players.has(guestInfo.playerId), true);
  // Voltou pela senha: o anfitrião é avisado e o carro volta a ser dele.
  const back = once(host, 'player:back', (b) => b.playerId === guestInfo.playerId);
  const g2 = await connect();
  g2.emit('room:rejoin', { roomCode, token: guestInfo.token });
  await back;
  host.close(); g2.close();
  await new Promise((r) => setTimeout(r, 100));
});

test('largada sincronizada: espera todos carregarem e manda o MESMO "vai"', async () => {
  const { host, guest } = await createRoomWithTwo();
  const [start] = await startRace(host, guest);
  assert.equal(start.sync, true);
  assert.equal(start.totalLoss, true);
  assert.equal(start.penalties, true);
  const waiting = once(guest, 'game:waiting', (d) => d.waiting.length === 1 && d.waiting[0] === 'Bruno');
  host.emit('player:loaded');
  await waiting;
  const goHost = once(host, 'game:go');
  const goGuest = once(guest, 'game:go');
  guest.emit('player:loaded');
  const [gh, gg] = await Promise.all([goHost, goGuest]);
  assert.equal(gh.holdMs, gg.holdMs);
  assert.ok(gh.holdMs >= 200 && gh.holdMs <= 3000);
  host.close(); guest.close();
  await new Promise((r) => setTimeout(r, 100));
});

test('placar oficial: o tempo final (com penalidade) decide, não a ordem da mensagem', async () => {
  const { host, guest, hostInfo, guestInfo } = await createRoomWithTwo();
  await startRace(host, guest);
  const finished = once(host, 'game:finished');
  host.emit('player:finished', { totalTime: 70, bestLap: 20, penalty: 0 });
  guest.emit('player:finished', { totalTime: 65, bestLap: 21, penalty: 5 });
  const { rankings } = await finished;
  assert.equal(rankings[0].id, guestInfo.playerId);
  assert.equal(rankings[1].id, hostInfo.playerId);
  assert.equal(rankings[0].penalty, 5);
  host.close(); guest.close();
  await new Promise((r) => setTimeout(r, 100));
});

test('reiniciar: todos aceitam → nova largada na MESMA sala e no mesmo grid', async () => {
  const { host, guest, roomCode } = await createRoomWithTwo();
  const [start1] = await startRace(host, guest);
  const vs = once(guest, 'vote:start');
  host.emit('vote:request', { kind: 'restart' });
  const v = await vs;
  assert.equal(v.kind, 'restart');
  assert.equal(v.byName, 'Ana');
  const res = once(host, 'vote:result');
  const restart = once(guest, 'game:start', (d) => d.round === start1.round + 1);
  guest.emit('vote:answer', { voteId: v.voteId, yes: true });
  assert.equal((await res).accepted, true);
  const start2 = await restart;
  assert.deepEqual(start2.startPositions.map((p) => [p.id, p.slot]), start1.startPositions.map((p) => [p.id, p.slot]));
  assert.equal(srv.rooms.rooms.get(roomCode).players.size, 2);
  host.close(); guest.close();
  await new Promise((r) => setTimeout(r, 100));
});

test('reiniciar: sem resposta no prazo = recusado e a corrida segue', async () => {
  const { host, guest, roomCode } = await createRoomWithTwo();
  await startRace(host, guest);
  const vs = once(guest, 'vote:start');
  host.emit('vote:request', { kind: 'restart' });
  await vs;
  const res = await once(host, 'vote:result', () => true, 3000);
  assert.equal(res.accepted, false);
  assert.deepEqual(res.no, ['Bruno']);
  assert.equal(srv.rooms.rooms.get(roomCode).session.ended, false);
  host.close(); guest.close();
  await new Promise((r) => setTimeout(r, 100));
});

test('jogar novamente: quem aceita continua NA MESMA SALA; quem não responde sai', async () => {
  const { host, guest, roomCode } = await createRoomWithTwo();
  const third = await connect();
  const tj = once(third, 'room:joined', (d) => d.token);
  third.emit('room:join', { roomCode, playerName: 'Caio' });
  await tj;
  const allReady = once(host, 'room:joined', (d) => d.players.length === 3 && d.players.filter((p) => !p.isHost).every((p) => p.ready));
  guest.emit('player:ready', { ready: true });
  third.emit('player:ready', { ready: true });
  await allReady;
  const s1 = once(guest, 'game:start');
  host.emit('room:start');
  const start1 = await s1;
  const early = once(guest, 'room:error');
  guest.emit('vote:request', { kind: 'rematch' });
  assert.match((await early).message, /resultado oficial/);
  const fin = once(guest, 'game:finished');
  host.emit('player:finished', { totalTime: 60 });
  guest.emit('player:finished', { totalTime: 61 });
  third.emit('player:finished', { totalTime: 62 });
  await fin;
  const vs = once(host, 'vote:start');
  guest.emit('vote:request', { kind: 'rematch' });
  const v = await vs;
  assert.equal(v.byName, 'Bruno');
  const kicked = once(third, 'room:kicked');
  const again = once(guest, 'game:start', (d) => d.round === start1.round + 1);
  host.emit('vote:answer', { voteId: v.voteId, yes: true });
  assert.match((await kicked).message, /nova partida/);
  const start2 = await again;
  assert.equal(start2.startPositions.filter((p) => !p.isAI).length, 2);
  assert.equal(srv.rooms.rooms.get(roomCode).players.size, 2);
  host.close(); guest.close(); third.close();
  await new Promise((r) => setTimeout(r, 100));
});
