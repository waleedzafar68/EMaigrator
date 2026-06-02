namespace EMaigrator.Connectors.Gmail;

/// <summary>Encodes/decodes Gmail's URL-safe base64 (RFC 4648 §5, no padding) used by format=raw.</summary>
public static class GmailRawCodec
{
    public static byte[] DecodeBase64Url(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s,
        };
        return Convert.FromBase64String(s);
    }

    public static string EncodeBase64Url(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
