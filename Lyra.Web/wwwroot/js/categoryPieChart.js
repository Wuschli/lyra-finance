window.categoryPieChart = (() => {
    const charts = {};

    const INACTIVE_COLOR = '#FCFCFC';

    const activeIndices = {};
    const chartColors = {};
    const globalMoveListeners = {};
    const tableHoverListeners = {};
    const wasInsideCanvas = {};

    function buildDataset(colors, values) {
        return {
            data: values,
            backgroundColor: [...colors],
            borderColor: 'transparent',
            borderWidth: 0,
            hoverOffset: 0,
            hoverBorderWidth: 0,
        };
    }

    function applySegmentHighlight(chart, canvasId, activeIndex) {
        const colors = chartColors[canvasId];
        const dataset = chart.data.datasets[0];
        const count = dataset.data.length;
        dataset.backgroundColor = Array.from({ length: count }, (_, i) => {
            if (activeIndex === -1 || i === activeIndex) return colors[i];
            return INACTIVE_COLOR;
        });
        chart.update('none');
    }

    function makeCenterLabelPlugin(canvasId, totalLabel) {
        return {
            id: 'centerLabel',
            afterDraw(chart) {
                const { ctx, chartArea: { left, top, right, bottom } } = chart;
                const cx = (left + right) / 2;
                const cy = (top + bottom) / 2;

                const activeIndex = activeIndices[canvasId] ?? -1;
                let label, amount, pct;

                if (activeIndex !== -1 && activeIndex < chart.data.datasets[0].data.length) {
                    const dataset = chart.data.datasets[0];
                    const total = dataset.data.reduce((a, b) => a + b, 0);
                    const value = dataset.data[activeIndex];
                    label = chart.data.labels[activeIndex];
                    amount = value.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' \u20AC';
                    pct = total > 0 ? ((value / total) * 100).toFixed(1) + '%' : '';
                } else {
                    label = totalLabel;
                    const total = chart.data.datasets[0].data.reduce((a, b) => a + b, 0);
                    amount = total.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' \u20AC';
                    pct = '';
                }

                const radius = Math.min(right - left, bottom - top) / 2;
                const labelSize = Math.max(10, radius * 0.13);
                const amountSize = Math.max(14, radius * 0.21);
                const pctSize = Math.max(9, radius * 0.12);

                const lineH = amountSize * 1.3;
                const startY = cy - lineH;

                ctx.save();
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';

                ctx.font = `${labelSize}px sans-serif`;
                ctx.fillStyle = 'rgba(128,128,128,0.9)';
                ctx.fillText(label, cx, startY);

                ctx.font = `bold ${amountSize}px sans-serif`;
                ctx.fillStyle = getComputedStyle(document.documentElement)
                    .getPropertyValue('--mud-palette-text-primary').trim() || '#222';
                ctx.fillText(amount, cx, startY + lineH);

                ctx.font = `${pctSize}px sans-serif`;
                ctx.fillStyle = 'rgba(128,128,128,0.9)';
                ctx.fillText(pct, cx, startY + lineH * 2);

                ctx.restore();
            }
        };
    }

    function buildOptions(canvasId, cutout, showLegend) {
        return {
            responsive: true,
            maintainAspectRatio: true,
            aspectRatio: 1,
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
                const newIndex = active.length > 0 ? active[0].index : -1;
                const prev = activeIndices[canvasId] ?? -1;
                if (newIndex === prev) return;
                activeIndices[canvasId] = newIndex;
                applySegmentHighlight(chart, canvasId, newIndex);
            },
        };
    }

    function resetChart(canvasId) {
        if ((activeIndices[canvasId] ?? -1) === -1) return;
        activeIndices[canvasId] = -1;
        const chart = charts[canvasId];
        if (chart) applySegmentHighlight(chart, canvasId, -1);
    }

    function createChart(canvasId, labels, values, colors, cutout, showLegend, totalLabel) {
        const existing = charts[canvasId];
        if (existing) {
            existing.destroy();
            delete charts[canvasId];
        }

        if (globalMoveListeners[canvasId]) {
            document.removeEventListener('mousemove', globalMoveListeners[canvasId]);
            delete globalMoveListeners[canvasId];
        }

        delete activeIndices[canvasId];
        delete chartColors[canvasId];
        delete wasInsideCanvas[canvasId];

        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        activeIndices[canvasId] = -1;
        chartColors[canvasId] = colors;
        wasInsideCanvas[canvasId] = false;

        charts[canvasId] = new Chart(canvas, {
            type: 'doughnut',
            data: { labels, datasets: [buildDataset(colors, values)] },
            options: buildOptions(canvasId, cutout, showLegend),
            plugins: [makeCenterLabelPlugin(canvasId, totalLabel)],
        });

        const onDocumentMove = (e) => {
            const el = document.getElementById(canvasId);
            if (!el) return;
            const rect = el.getBoundingClientRect();
            const inside = e.clientX >= rect.left && e.clientX <= rect.right
                && e.clientY >= rect.top && e.clientY <= rect.bottom;
            if (!inside && wasInsideCanvas[canvasId]) resetChart(canvasId);
            wasInsideCanvas[canvasId] = inside;
        };
        globalMoveListeners[canvasId] = onDocumentMove;
        document.addEventListener('mousemove', onDocumentMove);
    }

    function render(canvasId, labels, values, colors, totalLabel = 'Total') {
        createChart(canvasId, labels, values, colors, '78%', false, totalLabel);
    }

    function renderCompact(canvasId, labels, values, colors, totalLabel = '') {
        createChart(canvasId, labels, values, colors, '75%', false, totalLabel);
    }

    function bindTableHover(tbodyId, canvasIds) {
        unbindTableHover(tbodyId);

        const tbody = document.getElementById(tbodyId);
        if (!tbody) return;

        const ids = Array.isArray(canvasIds) ? canvasIds : [canvasIds];

        const onOver = (e) => {
            const row = e.target.closest('tr[data-segment-label]');
            if (!row) return;
            const segmentLabel = row.dataset.segmentLabel;
            for (const canvasId of ids) {
                const chart = charts[canvasId];
                if (!chart) continue;
                const index = chart.data.labels.indexOf(segmentLabel);
                if (index === -1) {
                    resetChart(canvasId);
                    continue;
                }
                activeIndices[canvasId] = index;
                applySegmentHighlight(chart, canvasId, index);
            }
        };

        const onOut = (e) => {
            if (e.relatedTarget && tbody.contains(e.relatedTarget)) return;
            for (const canvasId of ids) {
                resetChart(canvasId);
            }
        };

        tbody.addEventListener('mouseover', onOver);
        tbody.addEventListener('mouseout', onOut);
        tableHoverListeners[tbodyId] = { tbody, onOver, onOut };
    }

    function unbindTableHover(tbodyId) {
        const entry = tableHoverListeners[tbodyId];
        if (!entry) return;
        entry.tbody.removeEventListener('mouseover', entry.onOver);
        entry.tbody.removeEventListener('mouseout', entry.onOut);
        delete tableHoverListeners[tbodyId];
    }

    function destroy(canvasId) {
        if (globalMoveListeners[canvasId]) {
            document.removeEventListener('mousemove', globalMoveListeners[canvasId]);
            delete globalMoveListeners[canvasId];
        }

        const existing = charts[canvasId];
        if (existing) {
            existing.destroy();
            delete charts[canvasId];
        }
        delete activeIndices[canvasId];
        delete chartColors[canvasId];
        delete wasInsideCanvas[canvasId];
    }

    return { render, renderCompact, bindTableHover, unbindTableHover, destroy };
})();