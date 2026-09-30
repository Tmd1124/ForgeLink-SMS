// Every size in the app is relative to the root font size, so scaling it scales the whole UI.
window.applyTextScale = function (percent) {
    document.documentElement.style.fontSize = percent + "%";
};

window.applyTheme = function (mode, accentColor) {
    var resolvedMode = mode;
    if (mode === "System") {
        resolvedMode = (window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches) ? "Dark" : "Light";
    }
    document.body.setAttribute("data-theme", resolvedMode.toLowerCase());
    document.body.style.setProperty("--accent-color", accentColor);
};
