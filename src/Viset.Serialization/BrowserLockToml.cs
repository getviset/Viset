using Tomlyn;

namespace Viset.Serialization;

public static class BrowserLockToml
{
    public static BrowserLockTomlModel Deserialize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return TomlSerializer.Deserialize(source, TomlModelContext.Default.BrowserLockTomlModel)
            ?? throw new InvalidOperationException("Tomlyn returned no browser lock model.");
    }
}
