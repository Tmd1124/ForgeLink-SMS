// Turns a photo or video's bytes into a URL the in-app viewer can show, and frees it on close.
window.forgeLinkMedia = {
    url: null,
    open: function (bytes, type) {
        this.close();
        this.url = URL.createObjectURL(new Blob([bytes], { type: type }));
        return this.url;
    },
    close: function () {
        if (this.url) {
            URL.revokeObjectURL(this.url);
            this.url = null;
        }
    }
};

// Pinch, drag and double-tap zoom for the photo viewer; delegated so Blazor re-renders don't drop it.
(function () {
    const MAX = 6;
    const pts = new Map();
    let img = null, scale = 1, x = 0, y = 0, startDist = 0, startScale = 1, last = null, lastTap = 0;

    function apply() {
        if (scale <= 1) { scale = 1; x = 0; y = 0; }
        else {
            const r = img.parentElement.getBoundingClientRect();
            const mx = (img.offsetWidth * scale - r.width) / 2, my = (img.offsetHeight * scale - r.height) / 2;
            x = Math.max(-Math.max(mx, 0), Math.min(Math.max(mx, 0), x));
            y = Math.max(-Math.max(my, 0), Math.min(Math.max(my, 0), y));
        }
        img.style.transform = 'translate(' + x + 'px,' + y + 'px) scale(' + scale + ')';
    }
    function dist() {
        const [a, b] = [...pts.values()];
        return Math.hypot(a.x - b.x, a.y - b.y);
    }

    document.addEventListener('pointerdown', function (e) {
        const t = e.target.closest && e.target.closest('img[data-zoom]');
        if (!t) return;
        if (img !== t) { img = t; scale = 1; x = 0; y = 0; }
        t.setPointerCapture(e.pointerId);
        pts.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (pts.size === 2) { startDist = dist(); startScale = scale; }
        last = { x: e.clientX, y: e.clientY };
        if (pts.size === 1) {
            const now = Date.now();
            if (now - lastTap < 300) { scale = scale > 1 ? 1 : 2.5; apply(); }
            lastTap = now;
        }
    });
    document.addEventListener('pointermove', function (e) {
        if (!img || !pts.has(e.pointerId)) return;
        pts.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (pts.size === 2) {
            scale = Math.min(MAX, Math.max(1, startScale * dist() / startDist));
        } else if (scale > 1 && last) {
            x += e.clientX - last.x; y += e.clientY - last.y;
        }
        last = { x: e.clientX, y: e.clientY };
        apply();
    });
    function up(e) {
        pts.delete(e.pointerId);
        const rest = [...pts.values()][0];
        last = rest ? { x: rest.x, y: rest.y } : null;
    }
    document.addEventListener('pointerup', up);
    document.addEventListener('pointercancel', up);
    document.addEventListener('wheel', function (e) {
        const t = e.target.closest && e.target.closest('img[data-zoom]');
        if (!t) return;
        img = t;
        scale = Math.min(MAX, Math.max(1, scale * (e.deltaY < 0 ? 1.15 : 1 / 1.15)));
        apply();
    }, { passive: true });
})();
