using System.Text.Json;

namespace ForgeLinkSms.Core.Backup;

public static class BackupJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new BackupDamagedException();
        }
        catch (JsonException e)
        {
            throw new BackupDamagedException(e);
        }
    }
}
