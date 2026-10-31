// Brings an element into view; pages scroll inside the layout, so this finds the right scroller itself.
window.forgeLinkScroll = {
    intoView: function (id) {
        var el = document.getElementById(id);
        if (el) {
            el.scrollIntoView({ block: "start", behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
        }
    }
};
