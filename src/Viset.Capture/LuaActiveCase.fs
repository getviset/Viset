namespace Viset

open System

type internal ActiveCase =
    { Planned: PlannedCapture
      Session: CaptureSession
      AnimationUpdateDurations: ResizeArray<TimeSpan>
      mutable Snapshot: byte array option
      mutable Recorder: RecordingController option }

    override activeCase.ToString() = activeCase.Planned.OutputPath
