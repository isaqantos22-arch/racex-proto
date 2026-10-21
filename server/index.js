require('dotenv').config();
const express = require('express');
const http = require('http');
const { Server } = require('socket.io');
const RoomManager = require('./RoomManager');

function createServer(options = {}) {
  const app = express();
  const server = http.createServer(app);
  const io = new Server(server, { cors: { origin: '*' }, pingInterval: 10000, pingTimeout: 20000 });
  const rooms = new RoomManager(io, {
    maxRooms: Number(process.env.MAX_ROOMS) || 100,
    minPlayers: process.env.MIN_PLAYERS !== undefined ? Number(process.env.MIN_PLAYERS) : 2,
    ...options,
  });

  app.get('/health', (req, res) => res.json({ ok: true, rooms: rooms.count() }));

  io.on('connection', (socket) => {
    const on = (event, handler) => socket.on(event, (...args) => {
      try {
        handler(...args);
      } catch (err) {
        console.error(`[${event}]`, err);
      }
    });
    on('room:create', (d) => rooms.create(socket, d));
    on('room:join', (d) => rooms.join(socket, d));
    on('room:rejoin', (d) => rooms.rejoin(socket, d));
    on('room:return', () => rooms.returnToLobby(socket));
    on('player:join', (d) => rooms.updatePlayer(socket, d));
    on('player:ready', (d) => rooms.setReady(socket, d));
    on('room:settings', (d) => rooms.updateSettings(socket, d));
    on('room:start', () => rooms.start(socket));
    on('player:input', (d) => rooms.onInput(socket, d));
    on('player:position', (d) => rooms.onPosition(socket, d));
    on('ai:positions', (d) => rooms.onAiPositions(socket, d));
    on('player:finished', (d) => rooms.onFinished(socket, d));
    on('time:sync', (t, ack) => {
      if (typeof ack === 'function') ack({ client: t, server: Date.now() });
    });
    on('disconnect', () => rooms.leave(socket));
  });

  return {
    app,
    server,
    io,
    rooms,
    close: () => new Promise((resolve) => {
      rooms.dispose();
      io.close(() => resolve());
    }),
  };
}

if (require.main === module) {
  const { server } = createServer();
  const port = Number(process.env.PORT) || 3000;
  server.listen(port, () => console.log(`RaceX server ouvindo na porta ${port}`));
}

if (process.env.VERCEL) {
  const { server } = createServer();
  module.exports = server;
}

module.exports = { createServer };
