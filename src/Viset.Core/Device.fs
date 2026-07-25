namespace Viset

open System
open System.Diagnostics
open System.Globalization

type Dimensions =
    { Width: int
      Height: int }

    override dimensions.ToString() =
        String.Concat(
            dimensions.Width.ToString(CultureInfo.InvariantCulture),
            "x",
            dimensions.Height.ToString(CultureInfo.InvariantCulture)
        )

type Device =
    { Name: string
      Mobile: bool
      Touch: bool
      DeviceScale: double
      Viewport: Dimensions
      Frame: Dimensions option }

    override device.ToString() = device.Name
