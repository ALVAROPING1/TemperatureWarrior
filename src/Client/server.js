// @ts-check

import express from "express";

import http from 'http';
import path from 'path';
import url from 'url';
import util from 'util';
import WebSocket, { WebSocketServer } from 'ws';

const app = express();
const server = http.createServer(app);
const wss = new WebSocketServer({ server });

// Necesario para simular __filename y __dirname en un proyecto de
// nodejs que usa módulos ES6
/** @type {string} */
const __filename = url.fileURLToPath(import.meta.url);
/** @type {string} */
const __dirname = path.dirname(__filename);

/** @type {number} */
const PORT = 8080;

/** @type {function(Object): void} */
const log = obj => {
    console.log(util.inspect(obj, { showHidden: false, depth: null, colors: true }));
}

/** @type {function(WebSocket): function(Object): void}*/
const sender = ws => obj => {
    ws.send(JSON.stringify(obj))
}

// Express middleware
app.use("/", express.static(path.join(__dirname, "www")))

// WebSocket

wss.on('connection', ws => {
    ws.on('error', console.error);
    const send = sender(ws);

    ws.on('message', async msg => {
        const message = JSON.parse(msg.toString());
        log(message);
        switch (message.type) {
            case "Command":
                send({ type: "ConfigOK" });
                break;
            case "ShutdownCommand":
                send({ type: "ShutdownCommand", message: "Shutdown Command Received" });
                break;
            case "Start":
                for (let i = 0; i < 5; i++) {
                    send({ type: "N", ns: [10, 13] });
                    await new Promise((resolve) => {
                        setTimeout(resolve, 2000);
                    });
                }
                send({ type: "RoundFinished", timeInRange: 1000, ns: [15] });
                break;
            default:
                break;
        } {
        }
    });
});

server.listen(PORT, () => {
    console.log(`Server running at: 127.0.0.1:${PORT}`);
})
