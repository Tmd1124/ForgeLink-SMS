window.forgeLinkTwoPane = {
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
    }
};
