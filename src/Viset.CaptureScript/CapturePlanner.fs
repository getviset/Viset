namespace Viset

open System
open System.Collections.Generic
open System.IO
open Viset.Serialization

module internal CapturePlanner =
    open CaptureScriptPrimitives

    [<Literal>]
    let private SupportedVersion = 1L

    type private ResultBuilder() =
        member _.Bind(result, continuation) = Result.bind continuation result
        member _.Return value = Ok value
        member _.ReturnFrom result = result
        member _.Zero() = Ok()

    let private result = ResultBuilder()

    let private validateVersion (model: CaptureTomlModel) =
        if not model.Version.HasValue then
            error "version is required."
        elif model.Version.Value <> SupportedVersion then
            error (
                concat
                    [| "Unsupported capture version "
                       invariantInt64 model.Version.Value
                       "; expected "
                       invariantInt64 SupportedVersion
                       "." |]
            )
        else
            Ok()

    let private validateBrowserArguments (arguments: List<string>) =
        let conflicts =
            [ "--remote-debugging-port"; "--remote-debugging-pipe"; "--user-data-dir" ]

        arguments
        |> Seq.tryFind (fun argument ->
            String.IsNullOrWhiteSpace argument
            || argument |> Seq.exists Char.IsControl
            || conflicts
               |> List.exists (fun required ->
                   argument.Equals(required, StringComparison.OrdinalIgnoreCase)
                   || argument.StartsWith(String.Concat(required, "="), StringComparison.OrdinalIgnoreCase)))
        |> function
            | Some argument when String.IsNullOrWhiteSpace argument ->
                error "browser_arguments must not contain empty values."
            | Some argument when argument |> Seq.exists Char.IsControl ->
                error "browser_arguments must not contain control characters."
            | Some argument ->
                error (
                    concat
                        [| "browser_arguments contains '"
                           argument
                           "', which conflicts with mandatory browser launch isolation." |]
                )
            | None -> Ok(List.ofSeq arguments)

    let private resolveOutputRoot (request: CaptureRequest) scriptDirectory value =
        match request.OutputPath with
        | Some outputPath -> Ok outputPath
        | None when String.IsNullOrWhiteSpace value -> Ok scriptDirectory
        | None -> CaptureDeviceParser.resolveFrom scriptDirectory "output_root" value

    let private createCaptures format outputTemplate outputRoot devices axes data =
        let pathComparer =
            if OperatingSystem.IsWindows() then
                StringComparer.OrdinalIgnoreCase
            else
                StringComparer.Ordinal

        let outputPaths = HashSet<string> pathComparer

        let planCase (deviceName, device) matrixValues =
            let placeholders = ("device", TomlValue.String deviceName) :: matrixValues

            result {
                let! rendered = CaptureOutputTemplate.render outputTemplate placeholders
                let! relativePath = CaptureOutputTemplate.validateRelativePath rendered

                let absolutePath =
                    Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), outputRoot)

                if not (outputPaths.Add absolutePath) then
                    return! error (String.Concat("Expanded output path is duplicated: ", relativePath))
                else
                    return
                        { Format = format
                          OutputRelativePath = relativePath
                          OutputPath = absolutePath
                          Device = device
                          Axes = matrixValues
                          Data = data }
            }

        let matrixValues = CaptureOutputTemplate.expandAxes axes

        [ for device in devices do
              for values in matrixValues do
                  yield device, values ]
        |> traverse (fun _ (device, values) -> planCase device values)

    let create (request: CaptureRequest) scriptDirectory (model: CaptureTomlModel) =
        result {
            do! validateVersion model

            let! outputTemplate = requiredText "output" model.Output

            let! encoding = WebPOptionsParser.parse outputTemplate model.FramesPerSecond model.Webp

            let! frameSource = CaptureDeviceParser.parseFrameSource scriptDirectory model.Frame

            let! browserArguments = validateBrowserArguments model.BrowserArguments

            let! devices = CaptureDeviceParser.parseDevices frameSource model.Devices

            let! axes = CaptureOutputTemplate.parseAxes model.Matrix
            let! data = TomlValueParser.parseTable "data" model.Data

            let! outputRoot = resolveOutputRoot request scriptDirectory model.OutputRoot

            let! captures = createCaptures encoding.Format outputTemplate outputRoot devices axes data

            return
                { ScriptPath = request.ScriptPath
                  ScriptDirectory = scriptDirectory
                  OutputPath = outputRoot
                  FrameSource = frameSource
                  BrowserPath = request.BrowserPath
                  BrowserArguments = browserArguments
                  FramesPerSecond = encoding.FramesPerSecond
                  WebP = encoding.WebP
                  Captures = captures
                  Force = request.Force }
        }
