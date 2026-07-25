namespace Viset.EndToEnd

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets

type CommandResult =
    { ExitCode: int
      StandardOutput: string
      StandardError: string }

type TemporaryDirectory(path: string) =
    member _.Path = path

    interface IDisposable with
        member _.Dispose() =
            try
                Directory.Delete(path, true)
            with
            | :? IOException
            | :? UnauthorizedAccessException -> ()

module Fixtures =
    type private CommandTarget =
        { Executable: string
          PrefixArguments: string list }

    let private findRepositoryRoot startPath =
        let rec search directory =
            if File.Exists(Path.Combine(directory, "Viset.slnx")) then
                directory
            else
                match Directory.GetParent directory with
                | null -> invalidOp "Unable to locate the repository root from the end-to-end test runner."
                | parent -> search parent.FullName

        search (Path.GetFullPath startPath)

    let repositoryRoot =
        [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
        |> List.tryPick (fun path ->
            try
                Some(findRepositoryRoot path)
            with :? InvalidOperationException ->
                None)
        |> Option.defaultWith (fun () -> invalidOp "Unable to locate the repository root for end-to-end fixtures.")

    let fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures")

    let fixturePath relativePath = Path.Combine(fixtureRoot, relativePath)

    let createTemporaryDirectory label =
        let path =
            Path.Combine(Path.GetTempPath(), $"viset-end-to-end-{label}-{Guid.NewGuid():N}")

        Directory.CreateDirectory path |> ignore
        new TemporaryDirectory(path)

    let private executableFromPath (names: string list) =
        match Environment.GetEnvironmentVariable "PATH" |> Option.ofObj with
        | None -> None
        | Some path when String.IsNullOrWhiteSpace path -> None
        | Some path ->
            path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            |> Array.tryPick (fun directory ->
                names
                |> List.tryPick (fun name ->
                    let candidate = Path.Combine(directory, name)

                    if File.Exists candidate then
                        Some(Path.GetFullPath candidate)
                    else
                        None))

    let private configurationFromTestOutput () =
        let rec search (directory: DirectoryInfo) =
            match directory.Parent |> Option.ofObj with
            | None -> None
            | Some parent when String.Equals(parent.Name, "bin", StringComparison.OrdinalIgnoreCase) ->
                Some directory.Name
            | Some parent -> search parent

        DirectoryInfo(AppContext.BaseDirectory.TrimEnd Path.DirectorySeparatorChar)
        |> search
        |> Option.defaultValue "Debug"

    let private cliProject =
        Path.Combine(repositoryRoot, "src", "Viset.Cli", "Viset.Cli.fsproj")

    let private visetCommand =
        match Environment.GetEnvironmentVariable "VISET_END_TO_END_BINARY" |> Option.ofObj with
        | Some value when not (String.IsNullOrWhiteSpace value) ->
            { Executable = Path.GetFullPath value
              PrefixArguments = [] }

        | _ ->
            let dotnet =
                executableFromPath
                    [ if OperatingSystem.IsWindows() then
                          "dotnet.exe"
                      else
                          "dotnet" ]
                |> Option.defaultWith (fun () -> invalidOp "Unable to locate the dotnet executable on PATH.")

            { Executable = dotnet
              PrefixArguments =
                [ "run"
                  "--project"
                  cliProject
                  "--configuration"
                  configurationFromTestOutput ()
                  "--no-build"
                  "--" ] }

    // Preserved for existing tests that call:
    //
    //     run binaryPath [ ... ]
    //
    // `run` recognises this executable and prepends the dotnet-run arguments
    // when no explicit packaged binary was supplied.
    let binaryPath = visetCommand.Executable

    let browserPath =
        match Environment.GetEnvironmentVariable "VISET_BROWSER" |> Option.ofObj with
        | Some value when not (String.IsNullOrWhiteSpace value) -> value
        | _ ->
            executableFromPath [ "google-chrome"; "chromium"; "chromium-browser" ]
            |> Option.defaultWith (fun () ->
                invalidOp
                    "Set VISET_BROWSER to a compatible Chrome or Chromium executable before running end-to-end tests.")

    let pythonPath =
        match Environment.GetEnvironmentVariable "VISET_PYTHON" |> Option.ofObj with
        | Some value when not (String.IsNullOrWhiteSpace value) -> value
        | _ ->
            executableFromPath [ "python3" ]
            |> Option.defaultWith (fun () -> invalidOp "python3 is required by the end-to-end fixtures.")

    let browserArguments =
        match
            Environment.GetEnvironmentVariable "VISET_END_TO_END_BROWSER_ARGUMENTS"
            |> Option.ofObj
        with
        | Some value when not (String.IsNullOrWhiteSpace value) ->
            value.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map _.Trim()
            |> Array.filter (String.IsNullOrWhiteSpace >> not)
            |> Array.toList
        | _ -> []

    let private shellQuote (value: string) =
        String.Concat("'", value.Replace("'", "'\"'\"'", StringComparison.Ordinal), "'")

    let browserExecutable (directory: TemporaryDirectory) =
        match browserArguments with
        | [] -> browserPath

        | _ when OperatingSystem.IsWindows() ->
            invalidOp "VISET_END_TO_END_BROWSER_ARGUMENTS is supported only on Unix runners."

        | arguments ->
            let wrapper = Path.Combine(directory.Path, ".viset-browser")

            let command =
                [ "exec"; shellQuote browserPath ]
                @ (arguments |> List.map shellQuote)
                @ [ "\"$@\"" ]
                |> String.concat " "

            File.WriteAllText(wrapper, String.Concat("#!/bin/sh\n", command, "\n"))

            File.SetUnixFileMode(
                wrapper,
                UnixFileMode.UserRead
                ||| UnixFileMode.UserWrite
                ||| UnixFileMode.UserExecute
                ||| UnixFileMode.GroupRead
                ||| UnixFileMode.GroupExecute
                ||| UnixFileMode.OtherRead
                ||| UnixFileMode.OtherExecute
            )

            wrapper

    let assertBinaryExists () =
        match visetCommand.PrefixArguments with
        | [] when not (File.Exists visetCommand.Executable) ->
            invalidOp $"End-to-end binary does not exist: {visetCommand.Executable}."

        | _ when not (File.Exists cliProject) -> invalidOp $"Viset CLI project does not exist: {cliProject}."

        | _ when not (File.Exists visetCommand.Executable) ->
            invalidOp $"dotnet executable does not exist: {visetCommand.Executable}."

        | _ -> ()

    let private commandArguments executable arguments =
        if String.Equals(executable, binaryPath, StringComparison.Ordinal) then
            visetCommand.PrefixArguments @ arguments
        else
            arguments

    let run
        (executable: string)
        (arguments: string list)
        (workingDirectory: string)
        (environment: (string * string) list)
        (timeout: TimeSpan)
        : CommandResult =
        let startInfo =
            ProcessStartInfo(
                executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )

        startInfo.WorkingDirectory <- workingDirectory

        for argument in commandArguments executable arguments do
            startInfo.ArgumentList.Add argument

        for key, value in environment do
            startInfo.Environment[key] <- value

        use child = new Process(StartInfo = startInfo)

        if not (child.Start()) then
            invalidOp $"Unable to start {executable}."

        let standardOutput = child.StandardOutput.ReadToEndAsync()

        let standardError = child.StandardError.ReadToEndAsync()

        if not (child.WaitForExit(int timeout.TotalMilliseconds)) then
            child.Kill true

            invalidOp $"Timed out after {timeout} while running {executable}."

        { ExitCode = child.ExitCode
          StandardOutput = standardOutput.GetAwaiter().GetResult()
          StandardError = standardError.GetAwaiter().GetResult() }

    let runWithInput
        (executable: string)
        (arguments: string list)
        (workingDirectory: string)
        (environment: (string * string) list)
        (timeout: TimeSpan)
        (input: string)
        : CommandResult =
        let startInfo =
            ProcessStartInfo(
                executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )

        startInfo.WorkingDirectory <- workingDirectory

        for argument in commandArguments executable arguments do
            startInfo.ArgumentList.Add argument

        for key, value in environment do
            startInfo.Environment[key] <- value

        use child = new Process(StartInfo = startInfo)

        if not (child.Start()) then
            invalidOp $"Unable to start {executable}."

        child.StandardInput.Write input
        child.StandardInput.Close()

        let standardOutput = child.StandardOutput.ReadToEndAsync()

        let standardError = child.StandardError.ReadToEndAsync()

        if not (child.WaitForExit(int timeout.TotalMilliseconds)) then
            child.Kill true

            invalidOp $"Timed out after {timeout} while running {executable}."

        { ExitCode = child.ExitCode
          StandardOutput = standardOutput.GetAwaiter().GetResult()
          StandardError = standardError.GetAwaiter().GetResult() }

    let output result =
        String.Concat(result.StandardOutput, result.StandardError)

    let freePort () =
        use listener = new TcpListener(IPAddress.Loopback, 0)

        listener.Start()

        let port = (listener.LocalEndpoint :?> IPEndPoint).Port

        listener.Stop()
        port

    let isPortOpen port =
        use client = new TcpClient()

        try
            let connection = client.ConnectAsync(IPAddress.Loopback, port)

            connection.Wait 250 && client.Connected
        with
        | :? AggregateException
        | :? SocketException -> false

    let writeScript (directory: TemporaryDirectory) (name: string) (content: string) =
        let path = Path.Combine(directory.Path, name)

        File.WriteAllText(path, content)

        path

    let runCapture (directory: TemporaryDirectory) script outputPath arguments environment =
        assertBinaryExists ()

        let port = freePort ()

        let variables =
            [ "VISET_BROWSER", browserExecutable directory

              "VISET_PYTHON", pythonPath

              "VISET_FIXTURE_PORT", string port

              "VISET_FIXTURE_ROOT", fixtureRoot ]
            @ environment

        let result =
            run
                binaryPath
                ([ "capture"; script; "--output"; outputPath ] @ arguments)
                directory.Path
                variables
                (TimeSpan.FromMinutes 2.0)

        port, result
