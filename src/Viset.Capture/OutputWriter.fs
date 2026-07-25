namespace Viset

open System
open System.IO

module internal OutputWriter =
    let write force (captured: CapturedFile) =
        ArgumentNullException.ThrowIfNull captured.Bytes

        if captured.Bytes.Length = 0 then
            invalidArg (nameof captured) "Captured output bytes must not be empty."

        let path = captured.Capture.OutputPath
        OutputSafety.ensureTargetIsSafe path

        let parent =
            Path.GetDirectoryName path
            |> Option.ofObj
            |> Option.defaultValue Environment.CurrentDirectory

        Directory.CreateDirectory parent |> ignore
        OutputSafety.ensureExistingAncestorsAreNotLinked parent

        let temporaryPath =
            Path.Combine(parent, String.Concat $""".{Path.GetFileName path}.{Guid.NewGuid().ToString "N"}.tmp""")

        try
            File.WriteAllBytes(temporaryPath, captured.Bytes)
            File.Move(temporaryPath, path, force)
            path
        finally
            if File.Exists temporaryPath then
                File.Delete temporaryPath
