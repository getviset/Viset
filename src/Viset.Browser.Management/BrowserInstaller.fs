namespace Viset

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Threading
open System.Threading.Tasks

module internal BrowserInstaller =
    let private installLockTimeout = TimeSpan.FromMinutes 5.0
    let private installLockRetryDelay = TimeSpan.FromMilliseconds 100.0

    let unsupportedDiagnostic (runtimeIdentifier: string) =
        String.Concat(
            "Managed browser installation is not supported for ",
            runtimeIdentifier,
            "; set VISET_BROWSER or install Chrome, Chromium, or Edge on PATH."
        )

    let acquireInstallLockAsync (lockPath: string) (cancellationToken: CancellationToken) =
        task {
            let stopwatch = Stopwatch.StartNew()
            let mutable acquired: FileStream option = None

            while acquired.IsNone do
                cancellationToken.ThrowIfCancellationRequested()

                try
                    acquired <-
                        Some(
                            new FileStream(
                                lockPath,
                                FileMode.OpenOrCreate,
                                FileAccess.ReadWrite,
                                FileShare.None,
                                1,
                                FileOptions.Asynchronous
                            )
                        )
                with :? IOException when stopwatch.Elapsed < installLockTimeout ->
                    do! Task.Delay(installLockRetryDelay, cancellationToken)

                if acquired.IsNone && stopwatch.Elapsed >= installLockTimeout then
                    raise (
                        TimeoutException(
                            String.Concat(
                                "Timed out waiting for the managed browser install lock after ",
                                installLockTimeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                                " ms."
                            )
                        )
                    )

            return acquired.Value
        }

    let ensureUnixExecutable (executablePath: string) =
        if OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() then
            let mode = File.GetUnixFileMode executablePath
            File.SetUnixFileMode(executablePath, mode ||| UnixFileMode.UserExecute)

    let private fileSystemInfoIsLinkOrReparse (info: FileSystemInfo) =
        info.LinkTarget |> Option.ofObj |> Option.isSome
        || info.Exists && info.Attributes.HasFlag FileAttributes.ReparsePoint

    let private ensureNoLinkOrReparseAncestors (path: string) =
        let file = FileInfo(Path.GetFullPath path)

        if fileSystemInfoIsLinkOrReparse file then
            raise (InvalidDataException(String.Concat("Managed browser executable is a link or reparse point: ", path)))

        let mutable current = file.Directory |> Option.ofObj

        while current.IsSome do
            let directory = current.Value

            if fileSystemInfoIsLinkOrReparse directory then
                raise (
                    InvalidDataException(
                        String.Concat("Managed browser executable has a link or reparse ancestor: ", directory.FullName)
                    )
                )

            current <- directory.Parent |> Option.ofObj

    let private ensureNoLinkOrReparseDirectoryAncestors (path: string) =
        let mutable current = Some(DirectoryInfo(Path.GetFullPath path))

        while current.IsSome do
            let directory = current.Value

            if fileSystemInfoIsLinkOrReparse directory then
                raise (
                    InvalidDataException(
                        String.Concat("Managed browser target has a link or reparse ancestor: ", directory.FullName)
                    )
                )

            current <- directory.Parent |> Option.ofObj

    let private createPrivateDirectory (path: string) =
        Directory.CreateDirectory path |> ignore

        if OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() then
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

    let verifyExpectedExecutableAsync
        (browserLock: BrowserLock)
        (platform: BrowserPlatformLock)
        (targetDirectory: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let executable = BrowserCache.executablePath targetDirectory platform

            if not (File.Exists executable) then
                return
                    Error(
                        String.Concat(
                            "Browser archive did not contain the expected executable layout: ",
                            platform.ExecutableLayout
                        )
                    )
            elif File.GetAttributes(executable).HasFlag FileAttributes.ReparsePoint then
                return Error(String.Concat("Browser executable is a reparse point: ", executable))
            else
                try
                    ensureNoLinkOrReparseAncestors executable
                    ensureUnixExecutable executable

                    return!
                        BrowserValidation.validateBrowserAsync
                            ManagedCache
                            (Some browserLock.BrowserVersion)
                            executable
                            cancellationToken
                with error ->
                    return Error error.Message
        }

    let deleteDirectoryIfPresent (path: string) =
        if Directory.Exists path then
            Directory.Delete(path, true)

    let installUnderLockAsync
        (browserLock: BrowserLock)
        (platform: BrowserPlatformLock)
        (resolvedCacheRoot: string)
        (runtimeIdentifier: string)
        (workRoot: string)
        (downloadTimeout: TimeSpan)
        (cancellationToken: CancellationToken)
        =
        task {
            let targetDirectory =
                BrowserCache.targetDirectory resolvedCacheRoot browserLock runtimeIdentifier

            let targetInfo = DirectoryInfo targetDirectory

            if fileSystemInfoIsLinkOrReparse targetInfo then
                raise (
                    InvalidDataException(
                        String.Concat("Managed browser target is a link or reparse point: ", targetDirectory)
                    )
                )

            let! existingBrowserResult =
                task {
                    if Directory.Exists targetDirectory then
                        let! validation =
                            verifyExpectedExecutableAsync browserLock platform targetDirectory cancellationToken

                        match validation with
                        | Ok browser -> return Ok(Some browser)
                        | Error message -> return Error message
                    else
                        return Ok None
                }

            match existingBrowserResult with
            | Error message -> return Error message
            | Ok(Some browser) -> return Ok browser
            | Ok None ->
                createPrivateDirectory workRoot
                let archivePath = Path.Combine(workRoot, "browser.zip")
                let extractionRoot = Path.Combine(workRoot, "extracted")

                do! BrowserDownload.downloadAndVerifyAsync platform archivePath downloadTimeout cancellationToken

                do! BrowserArchive.extractArchiveAsync platform archivePath extractionRoot cancellationToken

                let! staged = verifyExpectedExecutableAsync browserLock platform extractionRoot cancellationToken

                match staged with
                | Error message -> return Error message
                | Ok _ ->
                    match Path.GetDirectoryName targetDirectory |> Option.ofObj with
                    | None -> return Error "Managed browser target directory has no parent."
                    | Some targetParent ->
                        Directory.CreateDirectory targetParent |> ignore
                        ensureNoLinkOrReparseDirectoryAncestors targetParent

                        let targetInfo = DirectoryInfo targetDirectory

                        if fileSystemInfoIsLinkOrReparse targetInfo then
                            return
                                Error(
                                    String.Concat(
                                        "Managed browser target is a link or reparse point: ",
                                        targetDirectory
                                    )
                                )
                        else
                            Directory.Move(extractionRoot, targetDirectory)

                            let! promoted =
                                verifyExpectedExecutableAsync browserLock platform targetDirectory cancellationToken

                            match promoted with
                            | Ok browser -> return Ok browser
                            | Error message ->
                                deleteDirectoryIfPresent targetDirectory
                                return Error message
        }
