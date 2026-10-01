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
