window.balanceChart = (() => {
    const charts = {};

    const COLOR_POS = '#4caf50';
    const COLOR_NEG = '#f44336';
    const COLOR_GRID = 'rgba(128,128,128,0.15)';
    const COLOR_ZERO = 'rgba(128,128,128,0.4)';

    function segmentColor(ctx) {
        const p0 = ctx.p0.parsed.y;
        const p1 = ctx.p1.parsed.y;
        return (p0 < 0 || p1 < 0) ? COLOR_NEG : COLOR_POS;
    }

    function pointColor(ctx) {
        const value = ctx.parsed?.y ?? ctx.dataset.data[ctx.dataIndex];
        return value < 0 ? COLOR_NEG : COLOR_POS;
    }

    function buildAnnotations(values) {
        const min = Math.floor(Math.min(...values) / 100) * 100;
        const max = Math.ceil(Math.max(...values) / 100) * 100;
        const annotations = {};

        for (let v = min; v <= max; v += 100) {
            const isZero = v === 0;
            annotations[`line_${v}`] = {
                type: 'line',
                yMin: v,
                yMax: v,
                borderColor: isZero ? COLOR_ZERO : COLOR_GRID,
                borderWidth: isZero ? 1.5 : 1,
                borderDash: isZero ? [] : [4, 4],
            };
        }

        return annotations;
    }

    function render(canvasId, labels, values) {
        const existing = charts[canvasId];
        if (existing) {
            existing.destroy();
            delete charts[canvasId];
        }

        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        charts[canvasId] = new Chart(canvas, {
            type: 'line',
            data: {
                labels,
                datasets: [{
                    data: values,
                    borderWidth: 2,
                    pointRadius: 0,
                    pointHoverRadius: 4,
                    pointBackgroundColor: pointColor,
                    fill: false,
                    tension: 0,
                    segment: {
                        borderColor: segmentColor,
                    },
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                interaction: {
                    mode: 'index',
                    intersect: false,
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            title: ctx => ctx[0]?.label ?? '',
                            label: ctx => ctx.parsed.y.toFixed(2) + ' \u20AC',
                        }
                    },
                    annotation: {
                        annotations: buildAnnotations(values),
                    },
                },
                scales: {
                    x: {
                        display: false,
                        ticks: { display: false },
                        grid: { display: false },
                    },
                    y: {
                        display: false,
                        ticks: { display: false },
                        grid: { display: false },
                    }
                },
                layout: {
                    padding: 0
                }
            }
        });
    }

    function destroy(canvasId) {
        const existing = charts[canvasId];
        if (existing) {
            existing.destroy();
            delete charts[canvasId];
        }
    }

    return { render, destroy };
})();