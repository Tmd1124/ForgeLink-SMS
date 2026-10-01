using Microsoft.Extensions.Logging;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms;

// Blazor only reports "An unhandled error has occurred"; this hands the logged exception to the
// error popup so the details can be read (and copied) on the phone itself.
public sealed class ErrorDetailsLoggerProvider : ILoggerProvider
{
    public static Action<string>? ShowDetails { get; set; }

    public ILogger CreateLogger(string categoryName) => new ErrorDetailsLogger();

    public void Dispose()
    {
    }

    private sealed class ErrorDetailsLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && exception is not null)
            {
                ShowDetails?.Invoke(ErrorReport.Format(exception));
                // A Blazor error breaks the screen until the app restarts, so it's reported like a crash.
                Platforms.Android.CrashReportService.Save(exception);
            }
        }
    }
}
