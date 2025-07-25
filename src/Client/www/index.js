// @ts-nocheck

/**
 * @typedef {Object} TemperatureRange
 * @property {number} tempMax
 * @property {number} tempMin
 * @property {number} roundTime
 */

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('rate').addEventListener('input', ranges_input);
    document.getElementById('kp').addEventListener('input', ranges_input);
    document.getElementById('ki').addEventListener('input', ranges_input);
    document.getElementById('kd').addEventListener('input', ranges_input);
    add_range();
    init_graph();
});

/** @returns {TemperatureRange[]} */
const get_ranges_values = () => {
    const ranges = document.getElementById('ranges');
    const range_values = [];
    for (let i = 0; i < ranges.childElementCount; i++) {
        const range = get_range_values(i);
        if (range == null) return null;
        range_values.push(range);
    }

    return range_values;
}

/** 
 * @param {number} range_num
 * @returns {TemperatureRange?}
 **/
const get_range_values = range_num => {
    const tempMin = parseFloat(document.getElementById(`min-temp${range_num}`).value);
    const tempMax = parseFloat(document.getElementById(`max-temp${range_num}`).value);
    const roundTime = parseInt(document.getElementById(`time-input${range_num}`).value);
    const range = document.getElementById(`range${range_num}`);

    if (isNaN(tempMin))
        return show_range_error(range, "Min debe ser un número válido");
    if (isNaN(tempMax))
        return show_range_error(range, "Max debe ser un número válido");
    if (isNaN(roundTime))
        return show_range_error(range, "Tiempo debe ser un número válido");
    if (tempMin < 12)
        return show_range_error(range, "Min debe estar por encima de 12ºC");
    if (tempMax > 30)
        return show_range_error(range, "Max debe estar por debado de 30ºC");
    if (tempMin > tempMax)
        return show_range_error(range, "Max debe estar por encima de Min");
    if (roundTime <= 0)
        return show_range_error(range, "Tiempo debe ser positivo");

    return { tempMax, tempMin, roundTime }
}

const show_range_error = (range, message) => {
    console.log('error:', message);
    hide_range_errors();
    range.querySelector('.error').textContent = message;
    return null;
};

const hide_range_errors = () => {
    document.querySelectorAll('.range .error')
        .forEach(elem => elem.textContent = '');
};

const remove_range = () => {
    const ranges = document.getElementById('ranges');
    if (ranges && ranges.childElementCount > 1)
        ranges.removeChild(ranges.lastChild);
}

const add_range = () => {
    const ranges = document.getElementById('ranges');
    const range_num = ranges.childElementCount;
    const range = document.createElement('div');
    range.classList.add('range');
    range.id = `range${range_num}`;
    range.innerHTML = `
        <h4>Range ${range_num + 1}<span class="error"></span></h4>
        <div>
            <label for="min-temp${range_num}">Min</label>
            <input type="number" id="min-temp${range_num}" min="12" max="30" step="0.1" value="12">
            <label for="max-temp${range_num}">Max</label>
            <input type="number" id="max-temp${range_num}" min="12" max="30" step="0.1" value="30">
            <label for="time-input${range_num}">Tiempo</label>
            <input type="number" id="time-input${range_num}" min="0" step="1" value="10">
        </div>
    `
    range.querySelectorAll('input')
        .forEach(elem => elem.addEventListener('input', ranges_input));
    ranges.appendChild(range);
};

const ranges_input = () => {
    if (!ranges_changed) {
        ranges_changed = true;
        if (connected && !round_started)
            document.getElementById("send-round").disabled = false;
    }
};

const show_connect_error = message => {
    // show the connection field if it was hidden
    document.querySelector('.server-settings .connect-field').classList.remove('hide');
    document.querySelector('.server-settings .connection-success')?.classList.add('hide');
    const message_div = document.querySelector('.server-settings .messages');
    message_div.classList.remove('hide');
    message_div.textContent = message;
};

const reset_element = element => {
    const new_elem = element.cloneNode(true);
    element.parentNode.replaceChild(new_elem, element);
};

const is_on_range = (temp, time) => {
    for (let i = 0; i < global_ranges.length; i++) {
        range = global_ranges[i];
        if (time > range.roundTime) {
            time -= range.roundTime;
        } else {
            return temp <= range.tempMax && temp >= range.tempMin;
        }
    }
    return false;
};

let running_interval;
let running_time;
let seconds_in_range_span;

/**
 * @param {string} status
 * @param {string|number|null} time_in_range */
const change_round_status = (status, time_in_range = null) => {
    status_span = document.getElementById('round-status');
    status_span.className = status;
    status_span.innerHTML = '';
    if (running_interval) clearInterval(running_interval);
    running_time = 0;

    switch (status) {
        case 'unset':
            status_span.innerText = 'Unset';
            document.getElementById('test-sensor').disabled = false;
            break;

        case 'ready':
            status_span.innerText = 'Ready';
            document.getElementById('test-sensor').disabled = true;
            break;

        case 'running':
            // set total time running span
            if (!round_is_test)
                document.getElementById('test-sensor').disabled = true;

            const time_span = document.createElement('span');
            time_span.appendChild(document.createTextNode('Time: '));
            const time = document.createElement('span');
            time.textContent = '0.0';
            time_span.appendChild(time);
            time_span.appendChild(document.createTextNode('s'));

            // set time left span
            const time_left = document.createElement('span');
            time_left.appendChild(document.createTextNode('Remaining: '));
            const left = document.createElement('span');
            left.textContent = '0.0';
            time_left.appendChild(left);
            time_left.appendChild(document.createTextNode('s'));

            running_interval = setInterval(() => {
                running_time += 0.1;
                time.textContent = running_time.toFixed(1);
                left.textContent = (total_time - running_time).toFixed(1);
            }, 100);

            // append all elements
            status_span.appendChild(document.createTextNode('Running'));
            status_span.appendChild(time_span);
            status_span.appendChild(time_left);

            if (round_is_test) break;
            // set seconds in range span
            const secs_in_span = document.createElement('span');
            secs_in_span.appendChild(document.createTextNode('Seconds in range: '));
            seconds_in_range_span = document.createElement('span');
            seconds_in_range_span.textContent = '0';
            secs_in_span.appendChild(seconds_in_range_span);
            secs_in_span.appendChild(document.createTextNode('s'));

            status_span.appendChild(secs_in_span);

            break;

        case 'finished':
            const total_time_span = document.createElement('span');
            total_time_span.textContent = `Time in Range: ${time_in_range}`;

            status_span.appendChild(document.createTextNode('Finished'));
            status_span.appendChild(total_time_span);
            document.getElementById('test-sensor').disabled = false;
            break;

        case 'mshutdown':
            status_span.innerText = 'Manual Shutdown';
            document.getElementById('test-sensor').disabled = false;
            break;

        case 'tshutdown':
            status_span.innerText = 'Temp. too high, Shutdown';
            document.getElementById('test-sensor').disabled = false;
            break;
    }
}

const stop_round = () => {
    round_started = false;
    document.getElementById("send-round").disabled = false;
    document.getElementById("start-round").disabled = true;
}
