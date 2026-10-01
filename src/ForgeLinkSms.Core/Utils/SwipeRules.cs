using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class SwipeRules
{
    // Shorter drags are treated as taps.
    public const double Threshold = 80;

    public static SwipeAction ActionFor(double deltaX, DisplaySettings settings) =>
        deltaX >= Threshold ? settings.SwipeRight
        : deltaX <= -Threshold ? settings.SwipeLeft
        : SwipeAction.Nothing;

    public static string Label(SwipeAction action) => action switch
    {
        SwipeAction.Archive => "Archive",
        SwipeAction.Trash => "Trash",
        _ => "Nothing"
    };

    public static string Summary(DisplaySettings settings) =>
        $"Right: {Label(settings.SwipeRight)} · Left: {Label(settings.SwipeLeft)}";
}
