using Tomlyn;
using Tomlyn.Model;

namespace Viset.Serialization;

public static class CaptureToml
{
    public static CaptureTomlModel Deserialize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var model =
            TomlSerializer.Deserialize(source, TomlModelContext.Default.CaptureTomlModel)
            ?? throw new InvalidOperationException("Tomlyn returned no capture v1 model.");

        RejectUnmapped(model.Unmapped, "capture");

        foreach (var (name, device) in model.Devices)
        {
            var devicePath = $"devices.{name}";
            RejectUnmapped(device.Unmapped, devicePath);
            RejectUnmapped(device.Viewport.Unmapped, $"{devicePath}.viewport");

            if (device.Frame is not null)
            {
                RejectUnmapped(device.Frame.Unmapped, $"{devicePath}.frame");
            }
        }

        if (model.Webp is not null)
        {
            RejectUnmapped(model.Webp.Unmapped, "webp");
        }

        return model;
    }

    private static void RejectUnmapped(TomlTable values, string path)
    {
        if (values.Count == 0)
        {
            return;
        }

        var name = values.Keys.First();
        throw new InvalidOperationException($"Unknown TOML property '{path}.{name}'.");
    }
}
