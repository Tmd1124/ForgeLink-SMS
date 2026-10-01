// Slides a chat row under the finger and reveals what letting go will do. The page still decides
// and performs the action on pointerup; this is only the visual. Rows carry their configured
// actions in data-swipe-right / data-swipe-left ("Nothing" while selecting).
(function () {
    var threshold = 80;
    var drag = null;

    var reset = function (row, wrap, animate) {
        row.style.transition = animate ? "transform 0.2s ease" : "";
        row.style.transform = "";
        var done = function () {
            row.style.transition = "";
            wrap.classList.remove("swiping", "to-right", "to-left", "armed");
        };
        if (animate) {
            setTimeout(done, 220);
        } else {
            done();
        }
    };

    document.addEventListener("pointerdown", function (e) {
        var row = e.target.closest ? e.target.closest("[data-swipe-row]") : null;
        if (!row || e.isPrimary === false) {
            return;
        }
        drag = { row: row, wrap: row.parentElement, x: e.clientX, y: e.clientY, horizontal: null, armed: false };
    }, true);

    document.addEventListener("pointermove", function (e) {
        if (!drag) {
            return;
        }
        var dx = e.clientX - drag.x;
        var dy = e.clientY - drag.y;
        if (drag.horizontal === null) {
            if (Math.abs(dx) < 10 && Math.abs(dy) < 10) {
                return;
            }
            drag.horizontal = Math.abs(dx) > Math.abs(dy);
            if (!drag.horizontal) {
                drag = null;
                return;
            }
        }
        var action = dx > 0 ? drag.row.dataset.swipeRight : drag.row.dataset.swipeLeft;
        // A direction set to Nothing only gives a little, so it feels like it won't go.
        var shown = action === "Nothing" ? Math.max(-36, Math.min(36, dx * 0.25)) : dx;
        drag.row.style.transition = "";
        drag.row.style.transform = "translateX(" + shown + "px)";
        drag.wrap.classList.add("swiping");
        drag.wrap.classList.toggle("to-right", dx > 0);
        drag.wrap.classList.toggle("to-left", dx < 0);
        var armed = action !== "Nothing" && Math.abs(dx) >= threshold;
        if (armed !== drag.armed) {
            drag.armed = armed;
            drag.wrap.classList.toggle("armed", armed);
            if (armed && navigator.vibrate) {
                navigator.vibrate(10);
            }
        }
    }, true);

    var release = function () {
        if (!drag) {
            return;
        }
        var d = drag;
        drag = null;
        if (!d.horizontal) {
            return;
        }
        if (d.armed) {
            // The page removes the row once the action runs; slide it out of the way meanwhile.
            var away = d.wrap.classList.contains("to-right") ? "110%" : "-110%";
            d.row.style.transition = "transform 0.18s ease-in";
            d.row.style.transform = "translateX(" + away + ")";
            setTimeout(function () {
                if (document.body.contains(d.row)) {
                    reset(d.row, d.wrap, true);
                }
            }, 700);
        } else {
            reset(d.row, d.wrap, true);
        }
    };
    document.addEventListener("pointerup", release, true);
    document.addEventListener("pointercancel", release, true);
})();
