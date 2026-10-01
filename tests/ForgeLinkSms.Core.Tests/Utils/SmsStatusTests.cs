using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SmsStatusTests
{
    [Theory]
    [InlineData(SmsStatus.TypeInbox, SmsStatus.StatusNone, false, SmsMessageStatus.Delivered)]
    [InlineData(SmsStatus.TypeOutbox, SmsStatus.StatusNone, true, SmsMessageStatus.Sending)]
    [InlineData(SmsStatus.TypeQueued, SmsStatus.StatusNone, true, SmsMessageStatus.Sending)]
    [InlineData(SmsStatus.TypeFailed, SmsStatus.StatusNone, true, SmsMessageStatus.Failed)]
    [InlineData(SmsStatus.TypeSent, SmsStatus.StatusNone, true, SmsMessageStatus.Sent)]
    [InlineData(SmsStatus.TypeSent, SmsStatus.StatusPending, true, SmsMessageStatus.Sent)]
    [InlineData(SmsStatus.TypeSent, SmsStatus.StatusComplete, true, SmsMessageStatus.Delivered)]
    [InlineData(SmsStatus.TypeSent, SmsStatus.StatusFailed, true, SmsMessageStatus.Sent)]
    public void Maps_the_stored_type_and_status_to_what_the_bubble_shows(int type, int status, bool outgoing, SmsMessageStatus expected)
    {
        var (isOutgoing, shown) = SmsStatus.FromProvider(type, status, TimeSpan.FromSeconds(5));

        Assert.Equal(outgoing, isOutgoing);
        Assert.Equal(expected, shown);
    }

    [Theory]
    [InlineData(SmsStatus.TypeOutbox)]
    [InlineData(SmsStatus.TypeQueued)]
    public void A_text_stuck_sending_for_ten_minutes_counts_as_not_sent(int type)
    {
        Assert.Equal(SmsMessageStatus.Sending, SmsStatus.FromProvider(type, SmsStatus.StatusNone, TimeSpan.FromMinutes(9)).Status);
        Assert.Equal(SmsMessageStatus.Failed, SmsStatus.FromProvider(type, SmsStatus.StatusNone, TimeSpan.FromMinutes(10)).Status);
    }

    [Theory]
    [InlineData(SmsStatus.BoxInbox, 1, false, SmsMessageStatus.Delivered)]
    [InlineData(SmsStatus.BoxSent, 1, true, SmsMessageStatus.Sent)]
    [InlineData(SmsStatus.BoxOutbox, 1, true, SmsMessageStatus.Sending)]
    [InlineData(SmsStatus.BoxOutbox, 10, true, SmsMessageStatus.Failed)]
    [InlineData(SmsStatus.BoxFailed, 1, true, SmsMessageStatus.Failed)]
    public void Maps_a_picture_message_box_to_what_the_bubble_shows(int box, int ageMinutes, bool outgoing, SmsMessageStatus expected)
    {
        var (isOutgoing, shown) = SmsStatus.FromMmsBox(box, TimeSpan.FromMinutes(ageMinutes));

        Assert.Equal(outgoing, isOutgoing);
        Assert.Equal(expected, shown);
    }

    [Theory]
    [InlineData(0x00, SmsStatus.StatusComplete)]
    [InlineData(0x02, SmsStatus.StatusComplete)]
    [InlineData(0x20, SmsStatus.StatusPending)]
    [InlineData(0x30, SmsStatus.StatusPending)]
    [InlineData(0x40, SmsStatus.StatusFailed)]
    [InlineData(0x60, SmsStatus.StatusFailed)]
    public void A_delivery_report_becomes_complete_pending_or_failed(int reportStatus, int expected) =>
        Assert.Equal(expected, SmsStatus.FromDeliveryReport(reportStatus));

    [Fact]
    public void Only_outgoing_texts_that_failed_can_be_retried()
    {
        SmsMessage Msg(bool outgoing, SmsMessageStatus status, bool mms = false) => new()
        {
            Id = 1, ThreadId = 1, Address = "555", Body = "hi", Timestamp = DateTimeOffset.UnixEpoch,
            IsOutgoing = outgoing, Status = status, IsMms = mms
        };

        Assert.True(SmsStatus.CanRetry(Msg(true, SmsMessageStatus.Failed)));
        Assert.True(SmsStatus.CanRetry(Msg(true, SmsMessageStatus.Failed, mms: true)));
        Assert.False(SmsStatus.CanRetry(Msg(true, SmsMessageStatus.Sent)));
        Assert.False(SmsStatus.CanRetry(Msg(true, SmsMessageStatus.Sending, mms: true)));
        Assert.False(SmsStatus.CanRetry(Msg(false, SmsMessageStatus.Failed)));
    }
}
