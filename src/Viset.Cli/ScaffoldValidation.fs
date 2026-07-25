namespace Viset

open System
open System.Globalization
open System.IO

module internal ScaffoldValidation =
    let private validateForcedTarget (request: InitRequest) =
        request.TargetDirectory
        |> ScaffoldFileSystem.generatedPaths
        |> List.tryFind Directory.Exists
        |> function
            | Some path -> Error $"A directory occupies a scaffold file path: {path}"

            | None -> Ok()

    let validateTarget (request: InitRequest) =
        if ScaffoldFileSystem.isLink request.TargetDirectory then
            Error $"Initialization target must not be a link: {request.TargetDirectory}"

        elif File.Exists(request.TargetDirectory) then
            Error $"Initialization target is a file: {request.TargetDirectory}"

        elif
            request.TargetDirectory
            |> ScaffoldFileSystem.generatedDirectoryPaths
            |> List.tryFind (fun path -> File.Exists(path) || ScaffoldFileSystem.isLink path)
            |> Option.isSome
        then
            Error "Scaffold editor-support directories must not be files or links."

        elif request.Force then
            validateForcedTarget request

        else
            let conflicts =
                request.TargetDirectory
                |> ScaffoldFileSystem.generatedPaths
                |> List.filter ScaffoldFileSystem.entryExists

            match conflicts with
            | [] -> Ok()

            | _ ->
                Error $"""Scaffold files already exist; use --force to replace them: {String.Join(", ", conflicts)}"""

    let validateAbsoluteUrl value =
        try
            let uri = Uri(value, UriKind.Absolute)

            if String.IsNullOrWhiteSpace(uri.Scheme) then
                Error "Page URL must be an absolute URL."
            else
                Ok value
        with :? UriFormatException ->
            Error "Page URL must be an absolute URL."

    let validateOutputPath value =
        let invalidCharacters =
            [| '<'; '>'; ':'; '"'; '|'; '?'; '*'; '#'; '!'; '['; ']'; '{'; '}' |]

        if String.IsNullOrWhiteSpace(value) then
            Error "Output file must not be empty."

        elif Path.IsPathRooted(value) || value.Contains('\\') then
            Error "Output file must be a project-relative path using forward slashes."

        elif value.IndexOfAny(invalidCharacters) >= 0 || value |> Seq.exists Char.IsControl then
            Error "Output file contains unsafe characters."

        elif not (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) then
            Error "The generated scaffold output file must end in .png."

        else
            let segments = value.Split('/', StringSplitOptions.None)

            if
                segments
                |> Array.exists (fun segment ->
                    String.IsNullOrEmpty(segment)
                    || segment = "."
                    || segment = ".."
                    || segment.StartsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith(' ')
                    || segment.EndsWith('.'))
            then
                Error "Output file must not contain empty, dot, or traversal segments."
            else
                Ok value

    let validateDimension label (value: string) =
        match Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, dimension when dimension > 0 -> Ok dimension
        | _ -> Error $"{label} must be a positive integer."
