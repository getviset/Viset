namespace Viset

open System
open System.Diagnostics
open System.Globalization

type CapturePerformanceMetrics =
    { Source: WebPSource
      Pipeline: WebPPipeline
      FrameCount: int
      UniqueFrameCount: int
      ActiveDuration: TimeSpan
      CaptureDurations: TimeSpan list
      MissedSlots: int
      DuplicatedFrames: int
      DroppedFrames: int }

    override metrics.ToString() =
        String.Concat(
            metrics.FrameCount.ToString(CultureInfo.InvariantCulture),
            " frames, ",
            metrics.ActiveDuration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
            " ms"
        )

type WebPProductionMetrics =
    { Encoder: WebPEncoder
      Pipeline: WebPPipeline
      FrameCount: int
      EncodedFrameCount: int
      SpilledFrameCount: int
      WorkerCount: int
      DecodeDurations: TimeSpan list
      EncodeDurations: TimeSpan list
      MuxDuration: TimeSpan
      TotalDuration: TimeSpan }

    override metrics.ToString() =
        String.Concat(
            metrics.FrameCount.ToString(CultureInfo.InvariantCulture),
            " frames via ",
            metrics.Encoder.ToString()
        )
