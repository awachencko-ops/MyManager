using System.Text;

namespace Replica.Shared;

public static class CurrentUserHeaderCodec
{
    public const string HeaderName = "X-Current-User";
    public const string EncodedHeaderName = "X-Current-User-Base64";

    public static bool RequiresEncoding(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var character in value)
        {
            if (character > 0x7F)
                return true;
        }

        return false;
    }

    public static string Encode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.Trim()));
    }

    public static string BuildAsciiFallback(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();
        return RequiresEncoding(normalized)
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized))
            : normalized;
    }

    public static bool TryDecode(string? encodedValue, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(encodedValue))
            return false;

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromBase64String(encodedValue.Trim())).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
