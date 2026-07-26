namespace Viset

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Viset.Serialization

module internal BrowserProcess =
    let private diagnosticReadTimeout = TimeSpan.FromMilliseconds 500.0
    let private processExitTimeout = TimeSpan.FromSeconds 5.0

    let private conflictingArguments =
        [| "--remote-debugging-port"; "--remote-debugging-pipe"; "--user-data-dir" |]

    let validateBrowserArguments (arguments: IReadOnlyList<string>) =
        arguments
        |> Seq.tryFind (fun argument ->
            String.IsNullOrWhiteSpace argument
            || conflictingArguments
               |> Array.exists (fun required ->
                   argument.Equals(required, StringComparison.OrdinalIgnoreCase)
                   || argument.StartsWith(
                       String.Concat(required, "="),
                       StringComparison.OrdinalIgnoreCase
                   )))
        |> function
            | Some argument when String.IsNullOrWhiteSpace argument ->
                invalidArg (nameof arguments) "Browser arguments must not contain empty values."
            | Some argument ->
                invalidArg
                    (nameof arguments)
                    (String.Concat(
                        "Browser argument '",
                        argument,
                        "' conflicts with the mandatory isolated CDP launch arguments."
                    ))
            | None -> ()

    let createStartInfo (options: BrowserSessionOptions) profilePath =
        let startInfo = ProcessStartInfo options.ExecutablePath
        startInfo.UseShellExecute <- false
        startInfo.CreateNoWindow <- true
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.ArgumentList.Add "--headless=new"
        startInfo.ArgumentList.Add "--remote-debugging-port=0"
        startInfo.ArgumentList.Add(String.Concat("--user-data-dir=", profilePath))
        startInfo.ArgumentList.Add "--no-first-run"
        startInfo.ArgumentList.Add "--no-default-browser-check"

        for argument in options.BrowserArguments do
            startInfo.ArgumentList.Add argument

        startInfo.ArgumentList.Add "about:blank"
        startInfo

    let readProcessDiagnosticsAsync (standardError: Task<string>) (standardOutput: Task<string>) =
        task {
            try
                let! diagnostics =
                    Task.WhenAll([| standardError; standardOutput |]).WaitAsync
                        diagnosticReadTimeout

                let errorText = diagnostics[0]
                let outputText = diagnostics[1]

                if not (String.IsNullOrWhiteSpace errorText) then
                    return Ok(errorText.Trim())
                elif not (String.IsNullOrWhiteSpace outputText) then
                    return Ok(outputText.Trim())
                else
                    return Ok "The browser produced no diagnostic output."
            with
            | :? TimeoutException ->
                return
                    Error(
                        String.Concat(
                            "Browser diagnostic streams did not close within ",
                            diagnosticReadTimeout.TotalMilliseconds.ToString(
                                "0",
                                CultureInfo.InvariantCulture
                            ),
                            " ms."
                        )
                    )
            | error ->
                return Error(String.Concat("Failed to read browser diagnostics: ", error.Message))
        }

    let private invariantInt (value: int) =
        value.ToString CultureInfo.InvariantCulture

    let waitForDevToolsPortAsync
        (browserProcess: Process)
        (profilePath: string)
        (standardError: Task<string>)
        (standardOutput: Task<string>)
        (timeout: TimeSpan)
        (cancellationToken: CancellationToken)
        =
        task {
            let activePortPath = Path.Combine(profilePath, "DevToolsActivePort")

            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeoutCancellation.CancelAfter timeout

            try
                let mutable port = None

                while port.IsNone do
                    if browserProcess.HasExited then
                        let! diagnosticsResult =
                            readProcessDiagnosticsAsync standardError standardOutput

                        let diagnostics =
                            match diagnosticsResult with
                            | Ok value -> value
                            | Error diagnosticError -> diagnosticError

                        raise (
                            InvalidOperationException(
                                String.Concat(
                                    "Browser exited before DevToolsActivePort was available: ",
                                    diagnostics
                                )
                            )
                        )

                    if File.Exists activePortPath then
                        let lines = File.ReadAllLines activePortPath

                        if lines.Length >= 2 then
                            match
                                Int32.TryParse(
                                    lines[0],
                                    NumberStyles.None,
                                    CultureInfo.InvariantCulture
                                )
                            with
                            | true, parsed when parsed > 0 -> port <- Some parsed
                            | _ ->
                                raise (
                                    InvalidDataException(
                                        String.Concat(
                                            "DevToolsActivePort contained an invalid port: ",
                                            lines[0]
                                        )
                                    )
                                )

                    if port.IsNone then
                        do! Task.Delay(50, timeoutCancellation.Token)

                return port.Value
            with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                return
                    raise (
                        TimeoutException(
                            String.Concat(
                                "Browser did not create DevToolsActivePort within ",
                                timeout.TotalMilliseconds.ToString(
                                    "0",
                                    CultureInfo.InvariantCulture
                                ),
                                " ms. Profile: ",
                                profilePath
                            )
                        )
                    )
        }

    let findPageEndpointAsync
        (port: int)
        (timeout: TimeSpan)
        (cancellationToken: CancellationToken)
        =
        task {
            use httpClient = new HttpClient()

            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeoutCancellation.CancelAfter timeout

            let targetListUri =
                Uri(String.Concat("http://127.0.0.1:", invariantInt port, "/json/list"))

            try
                let mutable endpoint = None

                while endpoint.IsNone do
                    let! json = httpClient.GetStringAsync(targetListUri, timeoutCancellation.Token)
                    let targets = CdpJson.DeserializeTargets json

                    endpoint <-
                        targets
                        |> Seq.tryFind (fun target ->
                            String.Equals(target.Type, "page", StringComparison.Ordinal)
                            && not (String.IsNullOrWhiteSpace target.WebSocketDebuggerUrl))
                        |> Option.map (fun target -> Uri target.WebSocketDebuggerUrl)

                    if endpoint.IsNone then
                        do! Task.Delay(50, timeoutCancellation.Token)

                return endpoint.Value
            with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                return
                    raise (
                        TimeoutException(
                            String.Concat(
                                "No page target appeared at ",
                                targetListUri.AbsoluteUri,
                                " within ",
                                timeout.TotalMilliseconds.ToString(
                                    "0",
                                    CultureInfo.InvariantCulture
                                ),
                                " ms."
                            )
                        )
                    )
        }

    let deleteProfileAsync profilePath =
        task {
            let mutable lastError = None
            let mutable attempt = 0

            while Directory.Exists profilePath && attempt < 5 do
                attempt <- attempt + 1

                try
                    Directory.Delete(profilePath, true)
                    lastError <- None
                with error ->
                    lastError <- Some error

                    if attempt < 5 then
                        do! Task.Delay 100

            match lastError with
            | Some error when Directory.Exists profilePath ->
                return
                    Some(
                        String.Concat(
                            "Failed to remove browser profile '",
                            profilePath,
                            "': ",
                            error.Message
                        )
                    )
            | _ -> return None
        }

    let cleanupProcessAsync (browserProcess: Process) =
        task {
            try
                if not browserProcess.HasExited then
                    browserProcess.Kill true

                use waitCancellation = new CancellationTokenSource(processExitTimeout)
                do! browserProcess.WaitForExitAsync waitCancellation.Token
                return true, None
            with error ->
                let processExited =
                    try
                        browserProcess.HasExited
                    with _ ->
                        false

                return
                    processExited,
                    Some(
                        String.Concat(
                            "Failed to terminate browser process ",
                            browserProcess.Id.ToString CultureInfo.InvariantCulture,
                            ": ",
                            error.Message
                        )
                    )
        }
