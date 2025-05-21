// @ts-nocheck

let chart;
let chart_output

const init_graph = () => {
    const ctx = document.getElementById('chart')?.getContext('2d');

    chart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Temperatura (°C)',
                    data: [],
                    borderWidth: 1,
                    pointBorderColor: [],
                    pointBackgroundColor: [],
                    segment: {
                        borderColor: seg => {
                            if (round_is_test) return '#5DADE2';
                            const p0 = is_on_range(seg.p0.parsed.y, seg.p0.parsed.x);
                            const p1 = is_on_range(seg.p1.parsed.y, seg.p1.parsed.x);
                            return p0 && p1 ? '#00FF88' : '#FF0000';
                        }
                    },
                },
            ],
        },
        options: {
            animation: false,
            scales: {
                y: {
                    title: {
                        display: true,
                        text: 'Temperatura (°C)',
                    },
                    min: 0,
                    max: 40,
                },
                x: {
                    type: 'linear',
                    title: {
                        display: true,
                        text: 'Tiempo (s)',
                    },
                    min: 0,
                },
            },
            plugins: {
                legend: { display: false },
            },
        },
    });

    const ctx2 = document.getElementById('chart-output')?.getContext('2d');
    chart_output = new Chart(ctx2, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Output',
                    data: [],
                    borderWidth: 1,
                    borderColor: '#000000',
                    backgroundColor: '#000000',
                },
                {
                    label: 'p',
                    data: [],
                    borderWidth: 1,
                    borderColor: '#ff0000',
                    backgroundColor: '#ff0000',
                },
                {
                    label: 'i',
                    data: [],
                    borderWidth: 1,
                    borderColor: '#00df00',
                    backgroundColor: '#00df00',
                },
                {
                    label: 'd',
                    data: [],
                    borderWidth: 1,
                    borderColor: '#00b0ff',
                    backgroundColor: '#00b0ff',
                },
            ],
        },
        options: {
            animation: false,
            scales: {
                y: {
                    title: {
                        display: true,
                        text: 'Output',
                    },
                    min: -2.5,
                    max: 2.5,
                },
                x: {
                    type: 'linear',
                    title: {
                        display: true,
                        text: 'Tiempo (s)',
                    },
                    min: 0,
                },
            },
            plugins: { legend: { display: false } },
        },
    });
};

/** @param {TemperatureRange[]} ranges */
const set_round_chart = ranges => {
    clear_graph();
    let t = 0;
    ranges.forEach(range => {
        chart.data.datasets.push({
            data: [{ x: t, y: range.tempMin }, { x: t + range.roundTime, y: range.tempMin }],
            pointRadius: 0,
            pointHitRadius: 0,
        });
        chart.data.datasets.push({
            data: [{ x: t, y: range.tempMax }, { x: t + range.roundTime, y: range.tempMax }],
            pointRadius: 0,
            pointHitRadius: 0,
            fill: '-1',
        });
        t += range.roundTime;
    });
    chart.options.scales.x.max = t;
    chart_output.options.scales.x.max = t;
    chart.update();
    chart_output.update();
};

const set_test_chart = () => {
    clear_graph();
    chart.options.scales.x.max = 60;

    chart.update();
    chart_output.update();
};

const add_chart_point = (x, y, i) => {
    chart.data.datasets[i ?? 0].data.push({ x, y });

    if (!round_is_test) {
        const on_range = is_on_range(y, x);
        const color = on_range ? '#00FF88' : '#FF0000';
        chart.data.datasets[0].pointBorderColor.push(color);
        chart.data.datasets[0].pointBackgroundColor.push(color);

        // also update the seconds in range span
        if (i === 0 && on_range) {
            let seconds = parseFloat(seconds_in_range_span.innerText);
            seconds_in_range_span.innerText = (seconds + refresh_rate).toFixed(1);
        }
    }
};

const clear_graph = () => {
    chart.data.datasets.length = 1; // remove all other datasets
    chart.data.datasets[0].data.length = 0; // remove temperature points

    // chart_output.data.datasets.length = 4; // remove all other datasets
    chart_output.data.datasets[0].data.length = 0; // remove output points
    chart_output.data.datasets[1].data.length = 0; // remove output points
    chart_output.data.datasets[2].data.length = 0; // remove output points
    chart_output.data.datasets[3].data.length = 0; // remove output points
}
