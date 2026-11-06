// A floating button the person can move: press and hold it, then drag. A quick tap still opens it, and where it was left
// is remembered (as a fraction of the area it floats in, so it survives folding, unfolding and rotating).
window.forgeLinkFab = {
    attach: function (id, storageKey) {
        var el = document.getElementById(id);
        if (!el || el.dataset.fabAttached) {
            return;
        }
        el.dataset.fabAttached = "1";

        var HOLD_MS = 450;
        var MOVE_SLOP = 10;
        var MARGIN = 8;
        var timer = null;
        var dragging = false;
        var startX = 0;
        var startY = 0;
        var grabX = 0;
        var grabY = 0;
        var swallowClick = false;

        // The box the button floats in: a transformed ancestor (the fold's two-pane layout makes one), else the window.
        function area() {
            for (var p = el.parentElement; p; p = p.parentElement) {
                if (getComputedStyle(p).transform !== "none") {
                    var r = p.getBoundingClientRect();
                    return { left: r.left, top: r.top, width: r.width, height: r.height };
                }
            }
            return { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
        }

        // left/top are relative to that box, clamped so the whole button stays on screen.
        function place(left, top) {
            var a = area();
            left = Math.max(MARGIN, Math.min(left, a.width - el.offsetWidth - MARGIN));
            top = Math.max(MARGIN, Math.min(top, a.height - el.offsetHeight - MARGIN));
            el.style.left = left + "px";
            el.style.top = top + "px";
            el.style.right = "auto";
            el.style.bottom = "auto";
        }

        function restore() {
            try {
                var saved = JSON.parse(localStorage.getItem(storageKey) || "null");
                if (saved && isFinite(saved.fx) && isFinite(saved.fy)) {
                    var a = area();
                    place(saved.fx * a.width, saved.fy * a.height);
                }
            } catch (e) {
                // Storage can be unavailable; the button just stays at its default spot.
            }
        }

        restore();
        window.addEventListener("resize", restore);

        el.addEventListener("pointerdown", function (e) {
            if (e.pointerType === "mouse" && e.button !== 0) {
                return;
            }
            startX = e.clientX;
            startY = e.clientY;
            clearTimeout(timer);
            timer = setTimeout(function () {
                timer = null;
                dragging = true;
                swallowClick = true;
                var r = el.getBoundingClientRect();
                grabX = startX - r.left;
                grabY = startY - r.top;
                el.classList.add("fab-dragging");
                try { el.setPointerCapture(e.pointerId); } catch (x) { }
                if (navigator.vibrate) {
                    navigator.vibrate(15);
                }
            }, HOLD_MS);
        });

        el.addEventListener("pointermove", function (e) {
            if (!dragging) {
                // Moving before the hold finishes is a swipe or a slipping finger, not a hold.
                if (timer && (Math.abs(e.clientX - startX) > MOVE_SLOP || Math.abs(e.clientY - startY) > MOVE_SLOP)) {
                    clearTimeout(timer);
                    timer = null;
                }
                return;
            }
            e.preventDefault();
            var a = area();
            place(e.clientX - grabX - a.left, e.clientY - grabY - a.top);
        });

        function end() {
            clearTimeout(timer);
            timer = null;
            if (!dragging) {
                return;
            }
            dragging = false;
            el.classList.remove("fab-dragging");
            var a = area();
            var r = el.getBoundingClientRect();
            try {
                localStorage.setItem(storageKey, JSON.stringify({ fx: (r.left - a.left) / a.width, fy: (r.top - a.top) / a.height }));
            } catch (x) { }
            // The release after a drag would otherwise count as a tap and open the chat.
            setTimeout(function () { swallowClick = false; }, 150);
        }

        el.addEventListener("pointerup", end);
        el.addEventListener("pointercancel", end);
        el.addEventListener("click", function (e) {
            if (swallowClick) {
                e.stopPropagation();
                e.preventDefault();
            }
        }, true);
        el.addEventListener("contextmenu", function (e) { e.preventDefault(); });
    }
};
