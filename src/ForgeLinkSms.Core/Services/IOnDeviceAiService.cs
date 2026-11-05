namespace ForgeLinkSms.Core.Services;

public enum OnDeviceAiStatus { Unsupported, NeedsDownload, Downloading, Ready }

public record OnDeviceAiState(OnDeviceAiStatus Status, string? Detail = null);

/// Google's Gemini Nano running on the phone through Android AICore: free, offline, and nothing leaves the device.
public interface IOnDeviceAiService
{
    Task<OnDeviceAiState> GetStateAsync();

    /// Downloads Gemini Nano once (Google manages the file). Reports bytes downloaded so far. Returns an error message, or null when done.
    Task<string?> DownloadAsync(IProgress<long>? progress);

    /// Runs one prompt on the phone. Returns the reply text, or throws OnDeviceAiException with a message fit to show.
    Task<string> GenerateAsync(string prompt, CancellationToken cancel = default);
}

public class OnDeviceAiException(string message) : Exception(message);
