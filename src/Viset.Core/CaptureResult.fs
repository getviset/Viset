namespace Viset

open System
open System.Diagnostics
open System.Globalization

type CaptureOutputResult =
    { Path: string
      Format: CaptureFormat
      FrameTicksMs: int list
      Performance: CapturePerformanceMetrics option
      WebPPerformance: WebPProductionMetrics option
      AnimationUpdateDurations: TimeSpan list }

    override result.ToString() = result.Path

type CaptureRunResult =
    { Outputs: CaptureOutputResult list }

    override result.ToString() =
        result.Outputs
        |> List.map (fun output -> output.Path)
        |> String.concat Environment.NewLine
