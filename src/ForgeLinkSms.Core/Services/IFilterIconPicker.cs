namespace ForgeLinkSms.Core.Services;

public interface IFilterIconPicker
{
    /// Lets the person choose a picture from the phone and returns it as a small square data URI, or null if they cancel or the picture can't be read.
    Task<string?> PickIconAsync();
}
