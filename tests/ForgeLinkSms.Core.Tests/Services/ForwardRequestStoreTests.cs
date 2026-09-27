using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ForwardRequestStoreTests
{
    [Fact]
    public void Take_hands_over_the_forwarded_message_once()
    {
        var store = new ForwardRequestStore();
        var photo = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };

        var request = new ForwardRequest("look at this", photo, "/conversations/thread?id=4&address=555", 4, "sms:10");
        store.Set(request);

        Assert.Equal(request, store.Take());
        Assert.Null(store.Take());
    }
}
