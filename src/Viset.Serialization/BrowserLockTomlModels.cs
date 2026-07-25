using Tomlyn.Serialization;

namespace Viset.Serialization;

public sealed record BrowserLockTomlModel(
    [property: TomlRequired] long? Version,
    [property: TomlRequired] string Publisher,
    [property: TomlRequired] string BrowserVersion,
    [property: TomlRequired] string Revision,
    [property: TomlRequired] Dictionary<string, BrowserLockPlatformTomlModel> Platforms
)
{
    public BrowserLockTomlModel()
        : this(
            Version: null,
            Publisher: null!,
            BrowserVersion: null!,
            Revision: null!,
            Platforms: new(StringComparer.Ordinal)
        ) { }
}

public sealed record BrowserLockPlatformTomlModel(
    [property: TomlRequired] string Url,
    [property: TomlRequired] string Sha256,
    [property: TomlRequired] string Executable
)
{
    public BrowserLockPlatformTomlModel()
        : this(Url: null!, Sha256: null!, Executable: null!) { }
}
