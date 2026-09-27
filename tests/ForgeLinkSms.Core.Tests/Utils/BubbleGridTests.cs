using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class BubbleGridTests
{
    [Theory]
    [InlineData(0, 84)]
    [InlineData(5, 84)]
    [InlineData(6, 64)]
    [InlineData(17, 64)]
    [InlineData(18, 52)]
    [InlineData(150, 52)]
    public void Recent_people_get_bigger_bubbles(int rank, int size) =>
        Assert.Equal(size, BubbleGrid.SizeFor(rank));
}
