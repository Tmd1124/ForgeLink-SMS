using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IDisplayStyleService
{
    DisplaySettings GetDisplaySettings();
    void SaveDisplaySettings(DisplaySettings settings);
}
