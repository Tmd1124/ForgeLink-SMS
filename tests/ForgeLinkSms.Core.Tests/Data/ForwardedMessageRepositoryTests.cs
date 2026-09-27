using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Data;

public class ForwardedMessageRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"forwarded-test-{Guid.NewGuid()}.db3");
    private readonly ForwardedMessageRepository _repository;

    public ForwardedMessageRepositoryTests()
    {
        _repository = new ForwardedMessageRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task A_forward_is_remembered_for_its_conversation_only()
    {
        await _repository.RecordAsync(4, "sms:10", "Kim Donnelly");
        await _repository.RecordAsync(9, "sms:11", "Ever Harris");

        var forwarded = await _repository.GetForThreadAsync(4);

        Assert.Equal(new Dictionary<string, string> { ["sms:10"] = "Kim Donnelly" }, forwarded);
    }

    [Fact]
    public async Task Forwarding_again_shows_the_latest_recipients()
    {
        await _repository.RecordAsync(4, "mms:3", "Kim Donnelly");
        await _repository.RecordAsync(4, "mms:3", "Ever Harris");

        Assert.Equal("Ever Harris", (await _repository.GetForThreadAsync(4))["mms:3"]);
    }

    [Fact]
    public void Sms_and_mms_messages_with_the_same_id_get_different_keys()
    {
        var sms = new SmsMessage { Id = 5, ThreadId = 1, Address = "5", Body = "a", Timestamp = DateTimeOffset.UtcNow, IsOutgoing = false, Status = SmsMessageStatus.Delivered, IsMms = false };
        var mms = new SmsMessage { Id = 5, ThreadId = 1, Address = "5", Body = "a", Timestamp = DateTimeOffset.UtcNow, IsOutgoing = false, Status = SmsMessageStatus.Delivered, IsMms = true };

        Assert.NotEqual(ForwardedMessage.KeyFor(sms), ForwardedMessage.KeyFor(mms));
    }
}
