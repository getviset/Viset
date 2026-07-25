namespace Viset

open System
open System.Diagnostics
open System.IO
open Viset.Serialization

type internal CaptureEncoding =
    { Format: CaptureFormat
      FramesPerSecond: int
      WebP: WebPOptions }

module internal WebPOptionsParser =
    open CaptureScriptPrimitives

    [<Literal>]
    let private DefaultFramesPerSecond = 30

    [<Literal>]
    let private MaximumFramesPerSecond = 60

    [<Literal>]
    let private MaximumWebPMethod = 6L

    [<Literal>]
    let private MaximumWebPQuality = 100.0

    [<Literal>]
    let private DefaultJpegSourceQuality = 95L

    let private isExecutable path =
        if not (File.Exists path) then
            false
        elif OperatingSystem.IsWindows() then
            true
        else
            let execute =
                UnixFileMode.UserExecute
                ||| UnixFileMode.GroupExecute
                ||| UnixFileMode.OtherExecute

            File.GetUnixFileMode path &&& execute <> enum<UnixFileMode> 0

    let private findFfmpeg () =
        let executableName =
            if OperatingSystem.IsWindows() then
                "ffmpeg.exe"
            else
                "ffmpeg"

        Environment.GetEnvironmentVariable "PATH"
        |> Option.ofObj
        |> Option.defaultValue String.Empty
        |> fun value -> value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        |> Seq.tryPick (fun directory ->
            try
                let path =
                    Path.Combine(directory.Trim().Trim '"', executableName) |> Path.GetFullPath

                if isExecutable path then Some path else None
            with
            | :? ArgumentException
            | :? NotSupportedException
            | :? PathTooLongException -> None)

    let private validateFfmpeg path =
        try
            let startInfo = ProcessStartInfo path
            startInfo.UseShellExecute <- false
            startInfo.CreateNoWindow <- true
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true

            for argument in [ "-hide_banner"; "-h"; "encoder=libwebp_anim" ] do
                startInfo.ArgumentList.Add argument

            use ffmpegProcess = new Process(StartInfo = startInfo)

            if not (ffmpegProcess.Start()) then
                Error "ffmpeg could not be started."
            else
                let standardOutput = ffmpegProcess.StandardOutput.ReadToEndAsync()
                let standardError = ffmpegProcess.StandardError.ReadToEndAsync()

                if not (ffmpegProcess.WaitForExit 5000) then
                    ffmpegProcess.Kill true
                    Error "ffmpeg did not respond to its encoder probe within five seconds."
                else
                    let output =
                        String.Concat(standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult())

                    if
                        ffmpegProcess.ExitCode = 0
                        && output.Contains("Encoder libwebp_anim", StringComparison.Ordinal)
                    then
                        Ok path
                    else
                        Error "ffmpeg does not provide the libwebp_anim encoder."
        with errorValue ->
            Error(String.Concat("ffmpeg could not be inspected: ", errorValue.Message))

    let private captureFormat (output: string) =
        if output.EndsWith(".png", StringComparison.OrdinalIgnoreCase) then
            Ok Png
        elif output.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) then
            Ok WebP
        else
            error "output must end in .png or .webp."

    let private framesPerSecond format (value: Nullable<int64>) =
        match format with
        | Png when value.HasValue -> error "frames_per_second is valid only for .webp output."
        | Png -> Ok DefaultFramesPerSecond
        | WebP when not value.HasValue -> Ok DefaultFramesPerSecond
        | WebP when value.Value < 1L || value.Value > int64 MaximumFramesPerSecond ->
            error (
                concat
                    [| "frames_per_second must be between 1 and "
                       invariantInt32 MaximumFramesPerSecond
                       "." |]
            )
        | WebP -> Ok(int value.Value)

    let private webPOptions format (model: WebpTomlModel | null) =
        match format, Option.ofObj model with
        | Png, Some _ -> error "webp configuration is valid only for .webp output."
        | Png, None -> Ok WebPOptions.Default
        | WebP, model ->
            let sourceName =
                model
                |> Option.map (fun value -> value.Source)
                |> Option.defaultValue String.Empty

            let sourceQuality =
                model |> Option.bind (fun value -> value.SourceQuality |> Option.ofNullable)

            let source =
                if String.IsNullOrWhiteSpace sourceName || sourceName = "png_screencast" then
                    match sourceQuality with
                    | Some _ -> error "webp.source_quality is valid only when webp.source = 'jpeg_screencast'."
                    | None -> Ok PngScreencast
                elif sourceName = "jpeg_screencast" then
                    let quality = sourceQuality |> Option.defaultValue DefaultJpegSourceQuality

                    if quality < 0L || quality > 100L then
                        error "webp.source_quality must be between 0 and 100."
                    else
                        Ok(JpegScreencast(int quality))
                else
                    error (
                        String.Concat(
                            "Unknown webp.source '",
                            sourceName,
                            "'; expected png_screencast or jpeg_screencast."
                        )
                    )

            let methodValue =
                model
                |> Option.bind (fun value -> value.Method |> Option.ofNullable)
                |> Option.defaultValue 0L

            let modeName =
                model
                |> Option.map (fun value -> value.Mode)
                |> Option.defaultValue String.Empty

            let modeDefaultQuality =
                if String.IsNullOrWhiteSpace modeName || modeName = "lossy" then
                    Ok("lossy", 75.0)
                elif modeName = "lossless" then
                    Ok("lossless", 50.0)
                else
                    error (String.Concat("Unknown webp.mode '", modeName, "'; expected lossy or lossless."))

            let qualityValue =
                model |> Option.bind (fun value -> value.Quality |> Option.ofNullable)

            let encoderName =
                model
                |> Option.map (fun value -> value.Encoder)
                |> Option.defaultValue String.Empty

            let pipelineName =
                model
                |> Option.map (fun value -> value.Pipeline)
                |> Option.defaultValue String.Empty

            if methodValue < 0L || methodValue > MaximumWebPMethod then
                error "webp.method must be between 0 and 6."
            else
                let encoder =
                    if String.IsNullOrWhiteSpace encoderName || encoderName = "libwebp_full" then
                        Ok LibWebPFull
                    elif encoderName = "libwebp_anim" then
                        Ok LibWebPAnim
                    elif encoderName = "ffmpeg" then
                        match findFfmpeg () with
                        | None ->
                            error (
                                "webp.encoder = 'ffmpeg' requires ffmpeg with the libwebp_anim encoder on PATH; "
                                + "Viset does not bundle ffmpeg."
                            )
                        | Some path ->
                            match validateFfmpeg path with
                            | Ok executable -> Ok(Ffmpeg executable)
                            | Error reason ->
                                error (
                                    String.Concat(
                                        "webp.encoder = 'ffmpeg' requires a usable ffmpeg with the libwebp_anim encoder; ",
                                        reason,
                                        " Viset does not bundle ffmpeg."
                                    )
                                )
                    else
                        error (
                            String.Concat(
                                "Unknown webp.encoder '",
                                encoderName,
                                "'; expected libwebp_full, libwebp_anim, or ffmpeg."
                            )
                        )

                let pipeline =
                    if String.IsNullOrWhiteSpace pipelineName || pipelineName = "spooled" then
                        Ok Spooled
                    elif pipelineName = "live" then
                        Ok Live
                    else
                        error (String.Concat("Unknown webp.pipeline '", pipelineName, "'; expected spooled or live."))

                match source, encoder, pipeline, modeDefaultQuality with
                | Error errors, _, _, _
                | _, Error errors, _, _
                | _, _, Error errors, _
                | _, _, _, Error errors -> Error errors
                | Ok selectedSource, Ok selectedEncoder, Ok selectedPipeline, Ok(mode, defaultQuality) ->
                    let quality = qualityValue |> Option.defaultValue defaultQuality

                    if not (Double.IsFinite quality) || quality < 0.0 || quality > MaximumWebPQuality then
                        error "webp.quality must be a finite number between 0 and 100."
                    else
                        let selectedMode =
                            if mode = "lossless" then
                                Lossless quality
                            else
                                Lossy quality

                        Ok
                            { Source = selectedSource
                              Encoder = selectedEncoder
                              Pipeline = selectedPipeline
                              Mode = selectedMode
                              Method = int methodValue }

    let parse output framesPerSecondValue model =
        match captureFormat output with
        | Error errors -> Error errors
        | Ok format ->
            match framesPerSecond format framesPerSecondValue, webPOptions format model with
            | Ok fps, Ok webP ->
                Ok
                    { Format = format
                      FramesPerSecond = fps
                      WebP = webP }
            | Error errors, _ -> Error errors
            | _, Error errors -> Error errors
