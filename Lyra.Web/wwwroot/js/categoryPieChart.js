window.categoryPieChart = (() => {
    const charts = {};

    const PALETTE = [
        '#4e79a7', '#f28e2b', '#e15759', '#76b7b2', '#59a14f',
        '#edc948', '#b07aa1', '#ff9da7', '#9c755f', '#bab0ac',
        '#d37295', '#fabfd2', '#8cd17d', '#b6992d', '#499894',
    ];

    const INACTIVE_COLOR = '#FCFCFC';

    function buildDataset(labels, values) {
        return {
            data: values,
            backgroundColor: labels.map((_, i) => PALETTE[i % PALETTE.length]),
            borderColor: 'transparent',
            borderWidth: 0,
            hoverOffset: 0,
            hoverBorderWidth: 0,
        };
    }

    // Updates segment colours: active index stays vivid, all others become INACTIVE_COLOR.
    // Pass activeIndex = -1 to restore all colours.
    function applySegmentHighlight(chart, activeIndex) {
        const dataset = chart.data.datasets[0];
        const count = dataset.data.length;
        dataset.backgroundColor = Array.from({ length: count }, (_, i) => {
            const base = PALETTE[i % PALETTE.length];
            if (activeIndex === -1 || i === activeIndex) return base;
            return INACTIVE_COLOR;
        });
        chart.update('none');
    }

    // Draws label/amount/pct in the donut centre.
    // Always renders all 3 lines to prevent layout shift on hover.
    function makeCenterLabelPlugin(totalLabel) {
        return {
            id: 'centerLabel',
            afterDraw(chart) {
                const { ctx, chartArea: { left, top, right, bottom } } = chart;
                const cx = (left + right) / 2;
                const cy = (top + bottom) / 2;

                const active = chart._active ?? [];
                let label, amount, pct;

                if (active.length > 0) {
                    const idx = active[0].index;
                    const dataset = chart.data.datasets[0];
                    const total = dataset.data.reduce((a, b) => a + b, 0);
                    const value = dataset.data[idx];
                    label = chart.data.labels[idx];
                    amount = value.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' \u20AC';
                    pct = total > 0 ? ((value / total) * 100).toFixed(1) + '%' : '';
                } else {
                    label = totalLabel;
                    const total = chart.data.datasets[0].data.reduce((a, b) => a + b, 0);
                    amount = total.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' \u20AC';
                    pct = ''; // empty – keeps layout stable
                }

                const radius = Math.min(right - left, bottom - top) / 2;
                const labelSize = Math.max(10, radius * 0.13);
                const amountSize = Math.max(14, radius * 0.21);
                const pctSize = Math.max(9, radius * 0.12);

                const lineH = amountSize * 1.3;
                // Always 3 lines → startY is always the same
                const startY = cy - lineH;

                ctx.save();
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';

                // Line 1 – category / total label
                ctx.font = `${labelSize}px sans-serif`;
                ctx.fillStyle = 'rgba(128,128,128,0.9)';
                ctx.fillText(label, cx, startY);

                // Line 2 – amount (bold, large)
                ctx.font = `bold ${amountSize}px sans-serif`;
                ctx.fillStyle = getComputedStyle(document.documentElement)
                    .getPropertyValue('--mud-palette-text-primary').trim() || '#222';
                ctx.fillText(amount, cx, startY + lineH);

                // Line 3 – percentage (empty string in resting state = no visible text, same height reserved)
                ctx.font = `${pctSize}px sans-serif`;
                ctx.fillStyle = 'rgba(128,128,128,0.9)';
                ctx.fillText(pct, cx, startY + lineH * 2);

                ctx.restore();
            }
        };
    }

    function buildOptions(cutout, showLegend) {
        return {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            cutout,
            plugins: {
                legend: showLegend ? {
                    display: true,
                    position: 'bottom',
                    labels: { boxWidth: 12, padding: 10, font: { size: 11 } }
                } : { display: false },
                tooltip: { enabled: false },
            },
            onHover(_, active, chart) {
                if (active.length > 0) {
                    applySegmentHighlight(chart, active[0].index);
                } else {
                    applySegmentHighlight(chart, -1);
                }
            },
        };
    }

    function createChart(canvasId, labels, values, cutout, showLegend, totalLabel) {
        const existing = charts[canvasId];
        if (existing) {
            existing.destroy();
            delete charts[canvasId];
        }

        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        charts[canvasId] = new Chart(canvas, {
            type: 'doughnut',
            data: { labels, datasets: [buildDataset(labels, values)] },
            options: buildOptions(cutout, showLegend),
            plugins: [makeCenterLabelPlugin(totalLabel)],
        });
    }

    function render(canvasId, labels, values, totalLabel = 'Total') {
        createChart(canvasId, labels, values, '78%', true, totalLabel);
    }

    function renderCompact(canvasId, labels, values, totalLabel = '') {
        createChart(canvasId, labels, values, '75%', false, totalLabel);
    }

    function destroy(canvasId) {
        chartTooltip.cleanup(canvasId);
        const existing = charts[canvasId];
        if (existing) {
            existing.destroy();
            delete charts[canvasId];
        }
    }

    return { render, renderCompact, destroy };
})();