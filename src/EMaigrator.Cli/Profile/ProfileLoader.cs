using System.Text.Json;
using System.Text.Json.Serialization;
using EMaigrator.Core.Model;   // ProviderId

namespace EMaigrator.Cli.Profile;

public static class ProfileLoader
{
    private static readonly string[] SecretKeyFragments =
        ["password", "secret", "token", "apikey", "key", "credential"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new ProviderIdJsonConverter(),
        },
    };

    public static ProfileLoadResult Load(string path)
    {
        if (!File.Exists(path))
            return ProfileLoadResult.Failed($"Profile file not found: {path}");

        MigrationProfile? profile;
        try
        {
            string json = File.ReadAllText(path);
            profile = JsonSerializer.Deserialize<MigrationProfile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ProfileLoadResult.Failed($"Profile file is not valid JSON: {ex.Message}");
        }

        if (profile is null)
            return ProfileLoadResult.Failed("Profile file deserialized to null.");

        foreach (ConnectionProfile side in new[] { profile.From, profile.To })
        {
            foreach (string settingKey in side.Settings.Keys)
            {
                string lower = settingKey.ToLowerInvariant();
                if (Array.Exists(SecretKeyFragments, frag => lower.Contains(frag, StringComparison.Ordinal)))
                {
                    return ProfileLoadResult.Failed(
                        $"Profile setting '{settingKey}' looks like a secret. " +
                        "Secrets must NOT be stored in the profile file. " +
                        "Pass them via an environment variable (EMAIGRATOR_SECRET_FROM / _TO) " +
                        "or the secure interactive prompt instead.");
                }
            }
        }

        return ProfileLoadResult.Success(profile);
    }
}

/// <summary>Serializes <see cref="ProviderId"/> as its bare string value ("imap"/"graph"/"gmail").</summary>
internal sealed class ProviderIdJsonConverter : JsonConverter<ProviderId>
{
    public override ProviderId Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        new(reader.GetString() ?? throw new JsonException("ProviderId must be a string."));

    public override void Write(Utf8JsonWriter writer, ProviderId value, JsonSerializerOptions o) =>
        writer.WriteStringValue(value.Value);
}
