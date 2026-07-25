namespace Viset

open System
open System.IO
open Viset.Serialization

module CaptureScript =
    open CaptureScriptPrimitives

    let plan (request: CaptureRequest) =
        if not (File.Exists request.ScriptPath) then
            error (String.Concat("Capture Lua does not exist: ", request.ScriptPath))
        else
            try
                let scriptDirectory =
                    Path.GetDirectoryName request.ScriptPath
                    |> Option.ofObj
                    |> Option.defaultValue Environment.CurrentDirectory

                let source = File.ReadAllText request.ScriptPath

                CaptureHeader.extract source
                |> Result.map CaptureTomlModels.Deserialize
                |> Result.bind (CapturePlanner.create request scriptDirectory)
            with errorValue ->
                error (String.Concat("Capture TOML could not be parsed: ", errorValue.Message))
