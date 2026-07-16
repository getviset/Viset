using Tomlyn;
using Tomlyn.Serialization;

namespace Viset.Serialization;

public static class BrowserLockTomlModels
{
    public static BrowserLockTomlModel Deserialize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return TomlSerializer.Deserialize(source, TomlModelContext.Default.BrowserLockTomlModel)
            ?? throw new InvalidOperationException("Tomlyn returned no browser lock model.");
    }
}

public sealed class BrowserLockTomlModel
{
    [TomlRequired]
    public long? Version { get; set; }

    [TomlRequired]
    public string Publisher { get; set; } = null!;

    [TomlRequired]
    public string BrowserVersion { get; set; } = null!;

    [TomlRequired]
    public string Revision { get; set; } = null!;

    [TomlRequired]
    public Dictionary<string, BrowserLockPlatformTomlModel> Platforms { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class BrowserLockPlatformTomlModel
{
    [TomlRequired]
    public string Url { get; set; } = null!;

    [TomlRequired]
    public string Sha256 { get; set; } = null!;

    [TomlRequired]
    public string Executable { get; set; } = null!;
}
