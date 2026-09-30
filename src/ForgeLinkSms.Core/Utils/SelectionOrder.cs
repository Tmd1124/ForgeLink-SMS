using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class SelectionOrder
{
    // While selecting, the chat that was long-pressed leads the list so it's in view; the rest keep their order.
    public static IEnumerable<SmsThread> PinFirst(IEnumerable<SmsThread> threads, long? pinnedId)
    {
        var list = threads as IReadOnlyList<SmsThread> ?? threads.ToList();
        var pinned = pinnedId is { } id ? list.FirstOrDefault(t => t.Id == id) : null;
        return pinned is null ? list : list.Where(t => t.Id != pinned.Id).Prepend(pinned);
    }
}
