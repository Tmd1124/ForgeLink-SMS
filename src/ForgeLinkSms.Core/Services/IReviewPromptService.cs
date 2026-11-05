namespace ForgeLinkSms.Core.Services;

public interface IReviewPromptService
{
    void RecordSent();

    // Called at a calm moment (back on the Chats list); asks Google Play only if a message was sent
    // since the last check and ReviewPromptRules says it's time.
    Task MaybeAskAsync();

    void OpenStoreListing();

    // The slim "Enjoying ForgeLink?" card for installs Google Play's own card never reaches.
    bool ShouldShowRatingCard();

    void DismissRatingCard();

    // Called after a rating reached the website: no more cards, no more Play asks.
    void MarkRated();
}
