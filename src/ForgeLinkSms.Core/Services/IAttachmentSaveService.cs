using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IAttachmentSaveService
{
    /// Saves a received attachment's full-quality bytes to the device's own Photos/Videos/
    /// Downloads collection (re-read fresh from its source part, not from any inlined preview
    /// data). Returns false on any failure rather than throwing, so the caller can show a plain
    /// "couldn't save" toast without needing to inspect an exception.
    Task<bool> SaveToDeviceAsync(MessageAttachment attachment);

    /// Copies a received attachment into the app's own storage so it can be sent again (forwarding).
    Task<PickedAttachment?> CopyForSendingAsync(MessageAttachment attachment);

    /// Opens a received photo or video in the phone's own viewer or player. False if it can't.
    Task<bool> OpenInViewerAsync(MessageAttachment attachment);

    /// The attachment's full bytes and content type, for showing it inside the app; null if it can't be read.
    Task<(byte[] Data, string ContentType)?> ReadAsync(MessageAttachment attachment);

    /// Plays a received video full screen inside the app. False if it can't be read.
    Task<bool> PlayVideoAsync(MessageAttachment attachment);
}
