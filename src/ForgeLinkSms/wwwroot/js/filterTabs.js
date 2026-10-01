// Long-press a filter tab to start arranging, then drag tabs to reorder them. Blazor owns the
// tabs: this only reports which tab is being dragged and the slot it's over, and the page
// re-renders them in the new order.
window.forgeLinkFilterTabs = {
    attach: function (container, dotnet) {
        if (!container || container._forgeLinkTabs) {
            return;
        }
        var state = { arranging: false, dragging: null, lastIndex: -1, timer: null, startX: 0, startY: 0 };
        container._forgeLinkTabs = state;
        var longPressMs = 450;
        var slop = 10;

        var tabFor = function (target) {
            return target && target.closest ? target.closest("[data-filter-id]") : null;
        };
        // The slot is how many other tabs sit left of the finger, measured at their midpoints.
        var slotAt = function (x) {
            var others = Array.prototype.filter.call(container.querySelectorAll("[data-filter-id]"), function (tab) {
                return tab.dataset.filterId !== state.dragging;
            });
            var slot = 0;
            while (slot < others.length) {
                var box = others[slot].getBoundingClientRect();
                if (x < box.left + box.width / 2) {
                    break;
                }
                slot++;
            }
            return slot;
        };
        var startDrag = function (tab) {
            state.dragging = tab.dataset.filterId;
            state.lastIndex = -1;
            state.arranging = true;
            if (navigator.vibrate) {
                navigator.vibrate(15);
            }
            dotnet.invokeMethodAsync("StartFilterDrag", Number(state.dragging));
        };
        var finish = function () {
            if (state.timer) {
                clearTimeout(state.timer);
                state.timer = null;
            }
            if (state.dragging) {
                state.dragging = null;
                dotnet.invokeMethodAsync("DropFilterTab");
            }
        };

        container.addEventListener("touchstart", function (e) {
            var tab = tabFor(e.target);
            if (!tab || e.touches.length !== 1) {
                return;
            }
            state.startX = e.touches[0].clientX;
            state.startY = e.touches[0].clientY;
            if (state.arranging) {
                startDrag(tab);
                return;
            }
            state.timer = setTimeout(function () {
                state.timer = null;
                startDrag(tab);
            }, longPressMs);
        }, { passive: true });

        // Not passive: once a drag starts, preventDefault stops the row from scrolling under the finger.
        container.addEventListener("touchmove", function (e) {
            var touch = e.touches[0];
            if (state.timer && (Math.abs(touch.clientX - state.startX) > slop || Math.abs(touch.clientY - state.startY) > slop)) {
                clearTimeout(state.timer);
                state.timer = null;
            }
            if (!state.dragging) {
                return;
            }
            e.preventDefault();
            var box = container.getBoundingClientRect();
            if (touch.clientX < box.left + 40) {
                container.scrollLeft -= 12;
            } else if (touch.clientX > box.right - 40) {
                container.scrollLeft += 12;
            }
            var slot = slotAt(touch.clientX);
            if (slot !== state.lastIndex) {
                state.lastIndex = slot;
                dotnet.invokeMethodAsync("MoveFilterTab", Number(state.dragging), slot);
            }
        }, { passive: false });

        container.addEventListener("touchend", finish);
        container.addEventListener("touchcancel", finish);
        container.addEventListener("contextmenu", function (e) {
            e.preventDefault();
        });
    },

    stopArranging: function (container) {
        if (container && container._forgeLinkTabs) {
            container._forgeLinkTabs.arranging = false;
        }
    }
};
