namespace ForgeLinkSms.Core.Utils;

// Only filters that have chats show as tabs, so a tab dragged among the tabs has to land in the
// right place in the full list of filters, hidden ones included.
public static class FilterOrder
{
    public static IReadOnlyList<long> MoveTab(IReadOnlyList<long> allIds, IReadOnlyList<long> tabIds, long movedId, int toTabIndex)
    {
        var otherTabs = tabIds.Where(id => id != movedId).ToList();
        var index = Math.Clamp(toTabIndex, 0, otherTabs.Count);
        var newTabs = otherTabs.ToList();
        newTabs.Insert(index, movedId);
        if (newTabs.SequenceEqual(tabIds))
        {
            return allIds;
        }

        var result = allIds.Where(id => id != movedId).ToList();
        if (index < otherTabs.Count)
        {
            result.Insert(result.IndexOf(otherTabs[index]), movedId);
        }
        else if (otherTabs.Count > 0)
        {
            result.Insert(result.IndexOf(otherTabs[^1]) + 1, movedId);
        }
        else
        {
            result.Add(movedId);
        }
        return result;
    }
}
