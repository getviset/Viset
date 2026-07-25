namespace Viset

open System
open System.Diagnostics
open System.Globalization

type CaptureRequest =
    { ScriptPath: string
      OutputPath: string option
      BrowserPath: string option
      Force: bool }

    override request.ToString() = request.ScriptPath
