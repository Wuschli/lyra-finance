window.chartTooltip = (() => {
    const GAP = 12;

    function getOrCreate(canvasId) {
        const tooltipId = `chartjs-tooltip-${canvasId}`;
        let el = document.getElementById(tooltipId);
        if (!el) {
            el = document.createElement('div');
            el.id = tooltipId;
            el.style.cssText = [
                'position:fixed',
                'pointer-events:none',
                'z-index:9999',
                'background:rgba(0,0,0,0.75)',
                'color:#fff',
                'border-radius:4px',
                'padding:6px 10px',
                'font-size:12px',
                'line-height:1.4',
                'white-space:nowrap',
                'transition:opacity 0.1s',
            ].join(';');
            document.body.appendChild(el);
        }
        return el;
    }

    // Positions the tooltip element so it stays fully inside the viewport.
    // Prefers right-of / below the anchor point and flips when needed.
    function position(el, anchorX, anchorY) {
        const elW = el.offsetWidth;
        const elH = el.offsetHeight;
        const vw = window.innerWidth;
        const vh = window.innerHeight;

        // Horizontal: prefer right, flip left
        let left = anchorX + GAP;
        if (left + elW > vw - GAP) {
            left = anchorX - elW - GAP;
        }

        // Vertical: center on anchor, clamp to viewport
        let top = anchorY - elH / 2;
        if (top < GAP) {
            top = GAP;
        } else if (top + elH > vh - GAP) {
            top = vh - elH - GAP;
        }

        el.style.left = `${left}px`;
        el.style.top = `${top}px`;
    }

    // Generic external tooltip handler for Chart.js.
    // Builds title + body lines from the Chart.js tooltip model.
    function handler(context) {
        const { chart, tooltip } = context;
        const el = getOrCreate(chart.canvas.id);

        if (tooltip.opacity === 0) {
            el.style.opacity = '0';
            return;
        }

        const lines = [];
        if (tooltip.title?.length) {
            lines.push(`<strong>${tooltip.title[0]}</strong>`);
        }
        tooltip.body?.forEach(b => b.lines.forEach(l => lines.push(l)));
        el.innerHTML = lines.join('<br>');

        const rect = chart.canvas.getBoundingClientRect();
        const anchorX = rect.left + tooltip.caretX;
        const anchorY = rect.top + tooltip.caretY;

        el.style.opacity = '1';
        position(el, anchorX, anchorY);
    }

    function cleanup(canvasId) {
        const el = document.getElementById(`chartjs-tooltip-${canvasId}`);
        if (el) el.remove();
    }

    return { handler, cleanup };
})();