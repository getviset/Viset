namespace Viset

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Threading
open System.Threading.Tasks

module internal BrowserValidation =
    let private versionTimeout = TimeSpan.FromSeconds 5.0
    let private diagnosticTimeout = TimeSpan.FromSeconds 1.0

    let private tryParseVersionToken (text: string) =
        text.Split([| ' '; '\t'; '\r'; '\n'; '('; ')'; ','; ';' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.tryPick (fun token ->
            let candidate = token.Trim()
            let segments = candidate.Split('.', StringSplitOptions.None)

            if
                segments.Length = 4
                && segments
                   |> Array.forall (fun segment -> segment.Length > 0 && segment |> Seq.forall Char.IsAsciiDigit)
            then
                Some candidate
            else
                None)

    let private readDiagnosticsAsync (standardOutput: Task<string>) (standardError: Task<string>) =
        task {
            try
                let! output = Task.WhenAll([| standardOutput; standardError |]).WaitAsync diagnosticTimeout

                return Ok(String.Concat(output[0], Environment.NewLine, output[1]).Trim())
            with
            | :? TimeoutException -> return Error "Browser version diagnostic streams did not close within 1000 ms."
            | error -> return Error(String.Concat("Failed to read browser version diagnostics: ", error.Message))
        }

    let readBrowserVersionAsync (executablePath: string) (cancellationToken: CancellationToken) =
        task {
            if String.IsNullOrWhiteSpace executablePath then
                return Error "Browser executable path must not be empty."
            elif not (File.Exists executablePath) then
                return Error(String.Concat("Browser executable does not exist: ", executablePath))
            else
                try
                    let startInfo = ProcessStartInfo(Path.GetFullPath executablePath)
                    startInfo.UseShellExecute <- false
                    startInfo.CreateNoWindow <- true
                    startInfo.RedirectStandardOutput <- true
                    startInfo.RedirectStandardError <- true
                    startInfo.ArgumentList.Add "--version"

                    use browserProcess =
                        Process.Start startInfo
                        |> Option.ofObj
                        |> Option.defaultWith (fun () ->
                            raise (InvalidOperationException "The browser version process could not be started."))

                    let standardOutput = browserProcess.StandardOutput.ReadToEndAsync()
                    let standardError = browserProcess.StandardError.ReadToEndAsync()

                    use timeoutCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource cancellationToken

                    timeoutCancellation.CancelAfter versionTimeout

                    let! exitResult =
                        task {
                            try
                                do! browserProcess.WaitForExitAsync timeoutCancellation.Token
                                return Ok()
                            with
                            | :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                                return Error "Browser version validation was cancelled."
                            | :? OperationCanceledException ->
                                try
                                    browserProcess.Kill true
                                with _ ->
                                    ()

                                return
                                    Error(
                                        String.Concat(
                                            "Browser executable did not answer --version within ",
                                            versionTimeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                                            " ms: ",
                                            executablePath
                                        )
                                    )
                        }

                    match exitResult with
                    | Error message -> return Error message
                    | Ok() ->
                        let! diagnosticsResult = readDiagnosticsAsync standardOutput standardError

                        match diagnosticsResult with
                        | Error message -> return Error message
                        | Ok diagnostics when browserProcess.ExitCode <> 0 ->
                            return
                                Error(
                                    String.Concat(
                                        "Browser executable --version failed with exit code ",
                                        browserProcess.ExitCode.ToString CultureInfo.InvariantCulture,
                                        ": ",
                                        diagnostics
                                    )
                                )
                        | Ok diagnostics ->
                            match tryParseVersionToken diagnostics with
                            | Some version -> return Ok version
                            | None ->
                                return
                                    Error(
                                        String.Concat(
                                            "Browser executable returned no four-part version from --version: ",
                                            executablePath
                                        )
                                    )
                with error ->
                    return
                        Error(
                            String.Concat(
                                "Browser executable could not be validated: ",
                                executablePath,
                                ": ",
                                error.Message
                            )
                        )
        }

    let validateBrowserAsync
        (origin: BrowserOrigin)
        (expectedVersion: string option)
        (executablePath: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let fullPath = Path.GetFullPath executablePath
            let! versionResult = readBrowserVersionAsync fullPath cancellationToken

            match versionResult with
            | Error message -> return Error message
            | Ok version ->
                match expectedVersion with
                | Some expected when not (String.Equals(version, expected, StringComparison.Ordinal)) ->
                    return
                        Error(
                            String.Concat("Browser executable reported version ", version, "; expected ", expected, ".")
                        )
                | _ ->
                    return
                        Ok
                            { ExecutablePath = fullPath
                              Origin = origin
                              Version = version }
        }

    let validateManagedBrowserAsync expectedVersion executablePath cancellationToken =
        task {
            try
                let file = FileInfo(Path.GetFullPath executablePath)

                if file.LinkTarget |> Option.ofObj |> Option.isSome then
                    return Error(String.Concat("Managed browser executable is a symbolic link: ", executablePath))
                elif file.Exists && file.Attributes.HasFlag FileAttributes.ReparsePoint then
                    return Error(String.Concat("Managed browser executable is a reparse point: ", executablePath))
                else
                    let mutable current = file.Directory |> Option.ofObj
                    let mutable unsafeAncestor = None

                    while current.IsSome && unsafeAncestor.IsNone do
                        let directory = current.Value

                        if
                            directory.LinkTarget |> Option.ofObj |> Option.isSome
                            || directory.Exists && directory.Attributes.HasFlag FileAttributes.ReparsePoint
                        then
                            unsafeAncestor <- Some directory.FullName

                        current <- directory.Parent |> Option.ofObj

                    match unsafeAncestor with
                    | Some path ->
                        return Error(String.Concat("Managed browser executable has a link or reparse ancestor: ", path))
                    | None ->
                        return!
                            validateBrowserAsync ManagedCache (Some expectedVersion) executablePath cancellationToken
            with error ->
                return Error(String.Concat("Managed browser cache validation failed: ", error.Message))
        }
