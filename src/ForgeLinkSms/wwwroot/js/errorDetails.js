// Filled from .NET whenever an error is logged; only visible once Blazor opens its error popup.
window.forgeLinkRecordError = function (details) {
    var pre = document.getElementById("blazor-error-details");
    if (pre) {
        pre.textContent = details;
    }
};

document.addEventListener("click", function (e) {
    if (e.target && e.target.id === "blazor-error-copy") {
        var text = document.getElementById("blazor-error-details").textContent;
        if (navigator.clipboard) {
            navigator.clipboard.writeText(text).then(function () { e.target.textContent = "Copied"; });
        }
    }
});

// Script errors never reach the .NET logger, so capture those here too.
window.addEventListener("error", function (e) {
    window.forgeLinkRecordError((e.error && e.error.stack) || (e.message + " at " + e.filename + ":" + e.lineno));
});
window.addEventListener("unhandledrejection", function (e) {
    var reason = e.reason;
    window.forgeLinkRecordError((reason && (reason.stack || reason.message)) || String(reason));
});

// Blazor WebView reports the unhandled .NET exception (message + stack) through console.error.
(function () {
    var original = console.error;
    console.error = function () {
        try {
            window.forgeLinkRecordError(Array.prototype.map.call(arguments, function (a) {
                return a && a.stack ? a.stack : String(a);
            }).join(" "));
        } catch (e) {
        }
        return original.apply(console, arguments);
    };
})();
