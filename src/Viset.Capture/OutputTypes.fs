namespace Viset

type CapturedFile =
    { Capture: PlannedCapture
      Bytes: byte array
      FrameTicksMs: int list }

    override file.ToString() = file.Capture.OutputPath
