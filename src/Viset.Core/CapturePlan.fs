namespace Viset

open System
open System.Diagnostics
open System.Globalization

type PlannedCapture =
    { Format: CaptureFormat
      OutputRelativePath: string
      OutputPath: string
      Device: Device
      Axes: (string * TomlValue) list
      Data: (string * TomlValue) list }

    override capture.ToString() = capture.OutputPath

type CapturePlan =
    { ScriptPath: string
      ScriptDirectory: string
      OutputPath: string
      FrameSource: FrameSource option
      BrowserPath: string option
      BrowserArguments: string list
      FramesPerSecond: int
      WebP: WebPOptions
      Captures: PlannedCapture list
      Force: bool }

    override plan.ToString() = plan.OutputPath
