using System.Text;

namespace ForgeLinkSms.Core.Utils;

public static class ErrorReport
{
    public static string Format(Exception exception)
    {
        var text = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current != exception)
            {
                text.AppendLine().Append("Caused by ");
            }
            text.Append(current.GetType().Name).Append(": ").AppendLine(current.Message);
            if (current.StackTrace is { } stack)
            {
                text.AppendLine(stack);
            }
        }
        return text.ToString().TrimEnd();
    }
}
