namespace ForgeLinkSms.Core.Utils;

public static class BubbleGrid
{
    // Bubble diameter in CSS pixels: the people you talk to most stand out, everyone else shrinks.
    public static int SizeFor(int rank) => rank switch
    {
        < 6 => 84,
        < 18 => 64,
        _ => 52
    };
}
