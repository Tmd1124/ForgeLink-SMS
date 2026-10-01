namespace ForgeLinkSms.Core.Services;

public interface IMediaThumbnailService
{
    /// An MMS image part scaled down to fit maxDimension pixels, as a data: URI; null if it can't be read.
    Task<string?> GetImageDataUriAsync(long partId, int maxDimension);

    /// A still frame from an MMS video part, scaled to fit maxDimension pixels, as a data: URI; null if it can't be read.
    Task<string?> GetVideoFrameDataUriAsync(long partId, int maxDimension);
}
