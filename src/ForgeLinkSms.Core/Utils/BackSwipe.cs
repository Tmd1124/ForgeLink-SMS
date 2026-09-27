namespace ForgeLinkSms.Core.Utils;

// A deliberate flick to the right, so vertical scrolling and slow drags (text selection)
// never navigate away by accident.
public static class BackSwipe
{
    private const double MinDistance = 80;
    private const double MaxElapsedMs = 800;

    public static bool IsBackSwipe(double dx, double dy, double elapsedMs) =>
        dx >= MinDistance && Math.Abs(dy) <= dx * 0.5 && elapsedMs <= MaxElapsedMs;
}
