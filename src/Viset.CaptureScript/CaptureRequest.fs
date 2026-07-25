namespace Viset

type CaptureRequest =
    { ScriptPath: string
      OutputPath: string option
      BrowserPath: string option
      Force: bool }

    override request.ToString() = request.ScriptPath
