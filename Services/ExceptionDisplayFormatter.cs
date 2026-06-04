namespace SandboxTimeline;

internal static class ExceptionDisplayFormatter
{
    public static string Format(Exception exception)
    {
        if (exception == null)
        {
            return "Unknown error (null exception).";
        }

        var details = exception.ToString();
        if (string.IsNullOrWhiteSpace(details))
        {
            return $"{exception.GetType().FullName} (no message)";
        }

        return details;
    }

    public static string FormatWithPrefix(string prefix, Exception exception)
    {
        return $"{prefix}{Environment.NewLine}{Environment.NewLine}{Format(exception)}";
    }
}
