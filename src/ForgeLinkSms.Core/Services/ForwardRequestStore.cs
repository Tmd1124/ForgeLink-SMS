using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

/// ReturnRoute is where to go after sending: the conversation a message was forwarded from, or
/// null for text shared in from another app.
/// SourceThreadId/SourceMessageKey identify the original message so it can be marked as forwarded.
public sealed record ForwardRequest(string? Text, PickedAttachment? Attachment, string? ReturnRoute = null, long? SourceThreadId = null, string? SourceMessageKey = null);

// Carries a message being forwarded from the conversation screen to the New Message screen.
public class ForwardRequestStore
{
    private ForwardRequest? _pending;

    public void Set(ForwardRequest request) => _pending = request;

    public void Set(string? text, PickedAttachment? attachment) => _pending = new ForwardRequest(text, attachment);

    public ForwardRequest? Take()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }
}
