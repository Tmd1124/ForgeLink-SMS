// Reports a finished horizontal touch to .NET, which decides whether it was a back swipe.
window.forgeLinkBackSwipe = {
    handlers: null,
    owner: null,
    // owner lets a page that is closing late leave the next page's listener alone.
    attach: function (owner, dotNetRef) {
        this.detach(this.owner);
        this.owner = owner;
        var start = null;

        function startsSomewhereElse(target) {
            if (target.closest("input, textarea, [contenteditable='true'], [data-no-back-swipe]")) {
                return true;
            }
            // A strip that can still scroll left should get the swipe instead of the page.
            for (var el = target; el && el !== document.body; el = el.parentElement) {
                var overflowX = getComputedStyle(el).overflowX;
                if ((overflowX === "auto" || overflowX === "scroll") && el.scrollLeft > 0) {
                    return true;
                }
            }
            return false;
        }

        var onStart = function (e) {
            if (e.touches.length !== 1 || startsSomewhereElse(e.target)) {
                start = null;
                return;
            }
            start = { x: e.touches[0].clientX, y: e.touches[0].clientY, t: Date.now() };
        };
        var onEnd = function (e) {
            if (!start || e.changedTouches.length !== 1) {
                return;
            }
            var touch = e.changedTouches[0];
            var dx = touch.clientX - start.x, dy = touch.clientY - start.y, elapsed = Date.now() - start.t;
            start = null;
            if (window.getSelection && window.getSelection().toString().length > 0) {
                return;
            }
            dotNetRef.invokeMethodAsync("OnHorizontalSwipe", dx, dy, elapsed);
        };
        document.addEventListener("touchstart", onStart, { passive: true });
        document.addEventListener("touchend", onEnd, { passive: true });
        this.handlers = { onStart: onStart, onEnd: onEnd };
    },
    detach: function (owner) {
        if (this.handlers && owner === this.owner) {
            document.removeEventListener("touchstart", this.handlers.onStart);
            document.removeEventListener("touchend", this.handlers.onEnd);
            this.handlers = null;
        }
    }
};
