window.forgeLinkTwoPane = {
    leftPctStorageKey: "forgeLinkTwoPaneLeftPct",

    watch: function (dotNetRef, minWidth) {
        this.dispose();
        var query = window.matchMedia("(min-width: " + minWidth + "px)");
        this.query = query;
        this.handler = function () { dotNetRef.invokeMethodAsync("OnWidthChanged", window.innerWidth); };
        query.addEventListener("change", this.handler);
        this.handler();
    },

    dispose: function () {
        if (this.query && this.handler) {
            this.query.removeEventListener("change", this.handler);
        }
        this.query = null;
        this.handler = null;
        this.disposeDivider();
    },

    getSavedLeftPct: function () {
        var raw = window.localStorage.getItem(this.leftPctStorageKey);
        var pct = raw === null ? NaN : parseFloat(raw);
        return isNaN(pct) ? null : pct;
    },

    // Re-attaches on every call, so it's safe to invoke from every render while two-pane mode is on.
    initDivider: function (dotNetRef, dividerId, leftId, containerId, minPct, maxPct) {
        this.disposeDivider();

        var divider = document.getElementById(dividerId);
        var left = document.getElementById(leftId);
        var container = document.getElementById(containerId);
        if (!divider || !left || !container) {
            return;
        }

        var dragging = false;

        var onPointerMove = function (e) {
            if (!dragging) {
                return;
            }
            var rect = container.getBoundingClientRect();
            var pct = ((e.clientX - rect.left) / rect.width) * 100;
            pct = Math.min(maxPct, Math.max(minPct, pct));
            left.style.flexBasis = pct + "%";
        };

        var onPointerUp = function () {
            if (!dragging) {
                return;
            }
            dragging = false;
            document.body.style.userSelect = "";
            var rect = container.getBoundingClientRect();
            var pct = parseFloat(left.style.flexBasis);
            if (!isNaN(pct)) {
                window.localStorage.setItem(window.forgeLinkTwoPane.leftPctStorageKey, pct);
                dotNetRef.invokeMethodAsync("OnLeftPctChanged", pct);
            }
        };

        var onPointerDown = function (e) {
            dragging = true;
            document.body.style.userSelect = "none";
            e.preventDefault();
        };

        divider.addEventListener("pointerdown", onPointerDown);
        document.addEventListener("pointermove", onPointerMove);
        document.addEventListener("pointerup", onPointerUp);

        this.dividerCleanup = function () {
            divider.removeEventListener("pointerdown", onPointerDown);
            document.removeEventListener("pointermove", onPointerMove);
            document.removeEventListener("pointerup", onPointerUp);
        };
    },

    disposeDivider: function () {
        if (this.dividerCleanup) {
            this.dividerCleanup();
            this.dividerCleanup = null;
        }
    }
};
