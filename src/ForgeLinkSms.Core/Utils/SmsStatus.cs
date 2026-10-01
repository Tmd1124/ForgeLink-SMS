using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

// Android's SMS store records a text's progress in its "type" (outbox → sent or failed) and,
// once the carrier reports back, its "status" column.
public static class SmsStatus
{
    public const int TypeInbox = 1;
    public const int TypeSent = 2;
    public const int TypeOutbox = 4;
    public const int TypeFailed = 5;
    public const int TypeQueued = 6;

    public const int StatusNone = -1;
    public const int StatusComplete = 0;
    public const int StatusPending = 32;
    public const int StatusFailed = 64;

    // Android reports a send within seconds; one still in the outbox this long lost its report (the
    // app was stopped mid-send, say), so it's offered for retry instead of spinning forever.
    public static readonly TimeSpan StuckSendingAfter = TimeSpan.FromMinutes(10);

    public static (bool IsOutgoing, SmsMessageStatus Status) FromProvider(int type, int status, TimeSpan age) => type switch
    {
        TypeInbox => (false, SmsMessageStatus.Delivered),
        TypeOutbox or TypeQueued => (true, age >= StuckSendingAfter ? SmsMessageStatus.Failed : SmsMessageStatus.Sending),
        TypeFailed => (true, SmsMessageStatus.Failed),
        // A failed delivery report still means the text left the phone, so resending could duplicate it.
        _ => (true, status == StatusComplete ? SmsMessageStatus.Delivered : SmsMessageStatus.Sent)
    };

    // GSM TP-Status: below 0x20 delivered, 0x20–0x3F still trying, 0x40 and up gave up.
    public static int FromDeliveryReport(int reportStatus) => reportStatus switch
    {
        < 0x20 => StatusComplete,
        < 0x40 => StatusPending,
        _ => StatusFailed
    };

    public static bool CanRetry(SmsMessage message) =>
        message is { IsOutgoing: true, IsMms: false, Status: SmsMessageStatus.Failed };
}
