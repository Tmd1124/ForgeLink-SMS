// Listens on document with capture so it catches the scroll wherever the WebView puts it
// (window or an inner container). Elements are looked up on every update because Blazor adds
// and removes the pond (search, select mode, lane changes) after observe() runs.
window.forgeLinkBubblePond = {
    handler: null,
    // headerIds: comma-separated pinned bars; the mini row sits under the lowest one showing.
    observe: function (pondId, miniId, headerIds) {
        this.dispose();
        var ticking = false;
        var update = function () {
            ticking = false;
            var pond = document.getElementById(pondId);
            var mini = document.getElementById(miniId);
            if (!pond || !mini) {
                return;
            }
            var headerBottom = 0;
            headerIds.split(",").forEach(function (id) {
                var header = document.getElementById(id);
                if (header) {
                    headerBottom = Math.max(headerBottom, header.getBoundingClientRect().bottom);
                }
            });
            mini.style.top = headerBottom + "px";
            mini.classList.toggle("show", pond.getBoundingClientRect().bottom < headerBottom + 40);
        };
        this.handler = function () {
            if (!ticking) {
                ticking = true;
                requestAnimationFrame(update);
            }
        };
        document.addEventListener("scroll", this.handler, { passive: true, capture: true });
        update();
    },
    // Selecting a row makes the pond disappear (and leaving selection brings it back), which
    // shifts every row by the pond's height. Remembering where a row sat on screen before that
    // render and scrolling by the difference afterward keeps it under the user's finger.
    anchor: null,
    rememberRow: function (rowId) {
        var row = document.getElementById(rowId);
        this.anchor = row ? { id: rowId, top: row.getBoundingClientRect().top } : null;
    },
    restoreRow: function () {
        var anchor = this.anchor;
        this.anchor = null;
        var row = anchor ? document.getElementById(anchor.id) : null;
        if (!row) {
            return;
        }
        var delta = row.getBoundingClientRect().top - anchor.top;
        if (delta !== 0) {
            this.scrollerFor(row).scrollTop += delta;
        }
    },
    // Selection puts the long-pressed row first in the list, so showing it means scrolling to the top.
    scrollListToTop: function (rowId) {
        var row = document.getElementById(rowId);
        if (row) {
            this.scrollerFor(row).scrollTop = 0;
        }
    },
    scrollerFor: function (row) {
        for (var el = row.parentElement; el; el = el.parentElement) {
            var overflowY = getComputedStyle(el).overflowY;
            if ((overflowY === "auto" || overflowY === "scroll") && el.scrollHeight > el.clientHeight) {
                return el;
            }
        }
        return document.scrollingElement || document.documentElement;
    },
    dispose: function () {
        if (this.handler) {
            document.removeEventListener("scroll", this.handler, { capture: true });
            this.handler = null;
        }
    }
};
