namespace ForgeLinkSms.Core.Services;

public interface ICrashReportService
{
    /// Details of a crash saved last time the app ran, or null.
    string? PendingCrash();

    /// Forgets the saved crash so the person is asked only once.
    void ClearPendingCrash();

    /// Opens the email app with the crash report filled in; nothing is sent until the person sends it.
    void EmailCrashReport(string details);

    /// Opens the email app with a feedback message and the app/phone details filled in.
    void EmailFeedback();

    /// Opens the email app addressed to AzureForge AI.
    void EmailCompany();

    /// Opens the email app with a star rating (1-5) and optional comment for AzureForge AI.
    void EmailRating(int stars, string? comment);
}
