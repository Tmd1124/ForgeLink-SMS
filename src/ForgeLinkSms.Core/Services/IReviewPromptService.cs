namespace ForgeLinkSms.Core.Services;

public interface IReviewPromptService
{
    void RecordSent();

    // Called at a calm moment (back on the Chats list); asks Google Play only if a message was sent
    // since the last check and ReviewPromptRules says it's time.
    Task MaybeAskAsync();

    void OpenStoreListing();
}
