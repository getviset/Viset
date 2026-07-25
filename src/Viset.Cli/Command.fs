namespace Viset

open System
open System.Diagnostics
open System.Globalization

type InitRequest =
    { TargetDirectory: string
      Interactive: bool
      Force: bool }

    override request.ToString() = request.TargetDirectory

[<DebuggerDisplay("Command")>]
type Command =
    | Capture of CaptureRequest
    | Init of InitRequest
    | BrowserInstall
    | Help
    | Version

    override command.ToString() =
        match command with
        | Capture request -> request.ScriptPath
        | Init request -> request.TargetDirectory
        | BrowserInstall -> "browser install"
        | Help -> "--help"
        | Version -> "--version"
