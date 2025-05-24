// @ts-check

const PASS = "gpFz#JD&It|BKNDV";
const mock_event = /** @type {Event} */ ({ preventDefault: () => { } });
const TWO_PHASE_INIT = true;

let ranges_changed = false;
let connected = false;
let round_started = false;
let round_updates_counter = 0;
let current_time = 0;
let refresh_rate = 0;
/** @type {TemperatureRange[]} */
let global_ranges;
let total_time = 0;
let round_is_test = false;
let initialized = false;
let init_finish = false;

const testBtn = /** @type {HTMLButtonElement}*/ (document.getElementById("test-sensor"));
const sendBtn = /** @type {HTMLButtonElement}*/ (document.getElementById("send-round"));
const startBtn = /** @type {HTMLButtonElement}*/ (document.getElementById("start-round"));

/**
 * @param {WebSocket} webSocket
 * @returns {function(MessageEvent): any} message
 */
function onMessage(webSocket) {
    return async e => {
        const json = await e.data.text?.() ?? e.data;
        console.log(json);
        let message;
        try {
            message = JSON.parse(json);
        } catch (error) {
            console.warn(error);
            return;
        }
        console.log(message.type);

        switch (message.type) {
            case 'N':
                console.log("received N");
                if (!initialized) {
                    initialized = true;
                    setTimeout(finish_init, 5000, webSocket)
                }
                if (!round_started || !init_finish) return;
                if (round_updates_counter == 0)
                    change_round_status('running');
                round_updates_counter += 1;
                for (const temp of message.ns) {
                    if (!isNaN(temp))
                        add_chart_point(current_time, temp);
                    current_time += refresh_rate;
                }
                chart.update();
                break;

            case 'TempTooHigh':
                stop_round();
                change_round_status('tshutdown');
                round_updates_counter = 0;

                if (round_is_test) {
                    testBtn.textContent = 'Test Sensor';
                    round_is_test = false;
                    change_round_status('unset');
                }

                break;

            case 'ShutdownCommand':
                stop_round();
                change_round_status('mshutdown');
                round_updates_counter = 0;

                if (round_is_test) {
                    testBtn.textContent = 'Test Sensor';
                    round_is_test = false;
                    change_round_status('unset');
                }

                break;

            case 'RoundFinished':
                console.log("Round finished");
                stop_round();
                if (round_is_test) {
                    testBtn.textContent = 'Test Sensor';
                    round_is_test = false; // para la próxima ronda
                    change_round_status('unset');
                } else {
                    const timeInSec = (message.timeInRange / 1000).toFixed(3);
                    change_round_status('finished', timeInSec);
                }

                round_updates_counter = 0;
                break;

            case 'Bad Format':
                console.log('mal formato');
                sendBtn.disabled = true;
                break;

            case 'ConfigOK':
                if (round_is_test) {
                    set_test_chart();
                    webSocket.send(JSON.stringify({ type: "Start" }));
                    round_started = true;
                } else {
                    set_round_chart(global_ranges);
                    ranges_changed = false;
                    startBtn.disabled = false;
                    change_round_status('ready');
                }
                current_time = 0;
                break;
            default:
                console.warn(`Mensaje no reconocido: ${json}`);
                break;
        }
    };
}

/**
 * @param {string} id
 * @returns {number?}
 **/
function get_field_value(id) {
    const elem = /** @type {HTMLButtonElement?} */ (document.getElementById(id));
    const value = parseInt(elem?.value ?? "");
    if (isNaN(value) || value <= 0) {
        elem?.classList.add('error');
        return null;
    }
    elem?.classList.remove('error');
    return value
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function sendRound(webSocket) {
    return e => {
        if (!webSocket.OPEN)
            throw new Error("Socket is closed");

        e.preventDefault();

        const ranges = get_ranges_values();
        if (!ranges) return;
        global_ranges = ranges;

        hide_range_errors();

        const refreshInMilliseconds = get_field_value("rate");
        if (refreshInMilliseconds == null) return;

        const message = {
            type: "Command",
            data: { refreshInMilliseconds, pass: PASS, ranges, isTest: false }
        };
        console.log(message)
        webSocket.send(JSON.stringify(message));

        refresh_rate = refreshInMilliseconds / 1000;
        sendBtn.disabled = true;
        sendBtn.textContent = 'Cambiar Ronda';
        total_time = ranges.reduce((acc, range) => acc + range.roundTime, 0);
    };
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function startRound(webSocket) {
    return e => {
        e.preventDefault();
        if (!webSocket.OPEN)
            throw new Error("Socket is closed");

        webSocket.send(JSON.stringify({ type: "Start" }));
        round_started = true;
        sendBtn.disabled = true;
        sendBtn.textContent = 'Enviar Ronda';
        startBtn.disabled = true;
    };
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function handleTest(webSocket) {
    return e => {
        e.preventDefault();
        if (!webSocket.OPEN)
            throw new Error("Socket is closed");


        if (!round_is_test) {
            round_is_test = true;
            sendBtn.disabled = true;
            testBtn.textContent = 'Stop Test';

            webSocket.send(JSON.stringify({
                type: "Command",
                data: {
                    refreshInMilliseconds: 1000,
                    pass: PASS,
                    isTest: true,
                    ranges: [{ tempMax: 30, tempMin: 12, roundTime: 60 }],
                }
            }));
            total_time = 60;
            refresh_rate = 1;
        } else {
            webSocket.send(JSON.stringify({ type: "Shutdown" }));
            testBtn.textContent = 'Test Sensor';
        }
    };
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function sendShutdown(webSocket) {
    return e => {
        e.preventDefault()

        if (!webSocket.OPEN)
            throw new Error("Socket is closed");

        webSocket.send(JSON.stringify({ type: "Shutdown" }));
    }
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function onOpen(webSocket) {
    return _ => {
        connected = true;
        console.log("===== CONNECTION OPEN =====");
        initialized = !TWO_PHASE_INIT;
        document.querySelector('.server-settings .connect-field')?.classList.add('hide');
        document.querySelector('.server-settings .messages')?.classList.add('hide');
        document.querySelector('.server-settings .connection-start')?.classList.remove('hide');
        if (TWO_PHASE_INIT)
            handleTest(webSocket)(mock_event);
        else
            update_connection_status();
    }
}

/**
 * @param {WebSocket} webSocket
 * @returns {void}
 */
function finish_init(webSocket) {
    handleTest(webSocket)(mock_event);
    update_connection_status();
}

function update_connection_status() {
    document.querySelector('.server-settings .connection-start')?.classList.add('hide');
    document.querySelector('.server-settings .connection-success')?.classList.remove('hide');
    init_finish = true;
    clear_graph();
}

/**
 * @param {WebSocket} _
 * @returns {function(CloseEvent): any}
 */
function onClose(_) {
    return e => {
        connected = false;
        console.log("===== CONNECTION CLOSED =====");
        show_connect_error(e.reason || "Conexión cerrada");

        sendBtn.disabled = true;
        startBtn.disabled = true;

        reset_element(sendBtn);
        reset_element(startBtn);

        change_round_status('unset');
        clear_graph();
    };
}

/**
 * @param {WebSocket} _
 * @returns {EventListener}
 */
function onError(_) {
    return e => {
        console.warn("===== CONNECTION ERROR =====");
        console.warn(e);
        show_connect_error("Error en la conexsión");
    };
}

function main() {
    const connectBtn = /** @type {HTMLButtonElement} */ (document.getElementById("server-connect"));
    connectBtn.addEventListener("click", e => {
        e.preventDefault();
        const input = /** @type {HTMLInputElement} */ (document.getElementById("ip"));
        const ip = input.value;
        console.log(`IP Server: ${ip}`);
        try {
            const webSocket = new WebSocket(`ws://${ip}/`);

            webSocket.addEventListener("open", onOpen(webSocket));
            webSocket.addEventListener("close", onClose(webSocket));
            webSocket.addEventListener("error", onError(webSocket));
            webSocket.addEventListener("message", onMessage(webSocket));

            const shutdownBtn = /** @type {HTMLButtonElement}*/ (document.getElementById("shutdown"));

            sendBtn.addEventListener("click", sendRound(webSocket));
            startBtn.addEventListener("click", startRound(webSocket));
            shutdownBtn.addEventListener("click", sendShutdown(webSocket));
            testBtn.addEventListener("click", handleTest(webSocket));

            sendBtn.disabled = false;
            shutdownBtn.disabled = false;
            testBtn.disabled = false;
        } catch {
            show_connect_error("Ruta del servidor invalida");
        }

    });
}

document.addEventListener("DOMContentLoaded", main);
