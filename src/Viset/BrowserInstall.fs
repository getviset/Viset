namespace Viset

open System
open System.Buffers
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.IO.Compression
open System.Net.Http
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks

module private BrowserInstallInternals =
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

    let downloadAndVerifyAsync
        (platform: BrowserPlatformLock)
        (archivePath: string)
        (downloadTimeout: TimeSpan)
        (cancellationToken: CancellationToken)
        =
        task {
            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)

            timeoutCancellation.CancelAfter downloadTimeout

            try
                use httpClient = new HttpClient()
                httpClient.Timeout <- Timeout.InfiniteTimeSpan

                use! response =
                    httpClient.GetAsync(
                        platform.Url,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutCancellation.Token
                    )

                response.EnsureSuccessStatusCode() |> ignore
                use! source = response.Content.ReadAsStreamAsync timeoutCancellation.Token

                use destination =
                    new FileStream(
                        archivePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous ||| FileOptions.SequentialScan
                    )

                use digest = IncrementalHash.CreateHash HashAlgorithmName.SHA256
                let buffer = ArrayPool<byte>.Shared.Rent 81920

                try
                    let mutable complete = false

                    while not complete do
                        let! read = source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCancellation.Token)

                        if read = 0 then
                            complete <- true
                        else
                            digest.AppendData(buffer, 0, read)
                            do! destination.WriteAsync(buffer.AsMemory(0, read), timeoutCancellation.Token)
                finally
                    ArrayPool<byte>.Shared.Return buffer

                do! destination.FlushAsync timeoutCancellation.Token

                let actualDigest =
                    digest.GetHashAndReset()
                    |> Convert.ToHexString
                    |> fun value -> value.ToLowerInvariant()

                if not (String.Equals(actualDigest, platform.Sha256, StringComparison.Ordinal)) then
                    raise (
                        InvalidDataException(
                            String.Concat(
                                "Browser archive SHA-256 mismatch for ",
                                platform.RuntimeIdentifier,
                                ": expected ",
                                platform.Sha256,
                                ", received ",
                                actualDigest,
                                "."
                            )
                        )
                    )
            with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                raise (
                    TimeoutException(
                        String.Concat(
                            "Browser download exceeded ",
                            downloadTimeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                            " ms."
                        )
                    )
                )
        }

    let private hasUnsafeSegments (entryName: string) =
        let segments = entryName.Split('/', StringSplitOptions.None)

        segments
        |> Array.mapi (fun index segment -> index, segment)
        |> Array.exists (fun (index, segment) ->
            let finalEmptyDirectorySegment = index = segments.Length - 1 && segment.Length = 0

            not finalEmptyDirectorySegment
            && (String.IsNullOrWhiteSpace segment
                || String.Equals(segment, ".", StringComparison.Ordinal)
                || String.Equals(segment, "..", StringComparison.Ordinal)
                || segment.Contains(':')))

    let private isLinkOrReparsePoint (entry: ZipArchiveEntry) =
        let unixFileType = (entry.ExternalAttributes >>> 16) &&& 0xF000
        let isUnixLink = unixFileType = 0xA000

        let isUnsupportedUnixType =
            unixFileType <> 0 && unixFileType <> 0x4000 && unixFileType <> 0x8000

        let isWindowsReparsePoint =
            entry.ExternalAttributes &&& int FileAttributes.ReparsePoint <> 0

        isUnixLink || isUnsupportedUnixType || isWindowsReparsePoint

    let private ensureNoReparseParents (extractionRoot: string) (targetDirectory: string) =
        let comparison =
            if OperatingSystem.IsWindows() then
                StringComparison.OrdinalIgnoreCase
            else
                StringComparison.Ordinal

        let root = Path.GetFullPath extractionRoot
        let mutable current = DirectoryInfo(Path.GetFullPath targetDirectory)
        let mutable reachedRoot = String.Equals(current.FullName, root, comparison)

        while not reachedRoot do
            if current.Attributes.HasFlag FileAttributes.ReparsePoint then
                raise (
                    InvalidDataException(
                        String.Concat(
                            "Browser archive extraction encountered a reparse-point directory: ",
                            current.FullName
                        )
                    )
                )

            match current.Parent |> Option.ofObj with
            | Some parent ->
                current <- parent
                reachedRoot <- String.Equals(current.FullName, root, comparison)
            | None -> raise (InvalidDataException "Browser archive extraction left the temporary root.")

    let extractArchiveAsync (archivePath: string) (extractionRoot: string) (cancellationToken: CancellationToken) =
        task {
            Directory.CreateDirectory extractionRoot |> ignore
            let root = Path.GetFullPath extractionRoot

            let rootPrefix =
                String.Concat(root.TrimEnd(Path.DirectorySeparatorChar), Path.DirectorySeparatorChar)

            let comparison =
                if OperatingSystem.IsWindows() then
                    StringComparison.OrdinalIgnoreCase
                else
                    StringComparison.Ordinal

            let comparer =
                if OperatingSystem.IsWindows() then
                    StringComparer.OrdinalIgnoreCase
                else
                    StringComparer.Ordinal

            let targets = HashSet<string>(comparer)

            use archiveStream =
                new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)

            use archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, false)

            for entry in archive.Entries do
                cancellationToken.ThrowIfCancellationRequested()
                let entryName = entry.FullName

                if String.IsNullOrWhiteSpace entryName then
                    raise (InvalidDataException "Browser archive contains an empty entry name.")

                if entryName.Contains('\\') then
                    raise (
                        InvalidDataException(
                            String.Concat("Browser archive entry contains a backslash and was rejected: ", entryName)
                        )
                    )

                if
                    Path.IsPathRooted entryName
                    || entryName.StartsWith("/", StringComparison.Ordinal)
                then
                    raise (InvalidDataException(String.Concat("Browser archive entry must be relative: ", entryName)))

                if hasUnsafeSegments entryName then
                    raise (InvalidDataException(String.Concat("Browser archive entry has an unsafe path: ", entryName)))

                if isLinkOrReparsePoint entry then
                    raise (
                        InvalidDataException(
                            String.Concat("Browser archive entry is a link or reparse point: ", entryName)
                        )
                    )

                let relativePath = entryName.Replace('/', Path.DirectorySeparatorChar)
                let destinationPath = Path.GetFullPath(Path.Combine(root, relativePath))

                if not (destinationPath.StartsWith(rootPrefix, comparison)) then
                    raise (
                        InvalidDataException(
                            String.Concat("Browser archive entry escapes the extraction root: ", entryName)
                        )
                    )

                if not (targets.Add destinationPath) then
                    raise (
                        InvalidDataException(String.Concat("Browser archive contains a duplicate entry: ", entryName))
                    )

                let isDirectory = entryName.EndsWith("/", StringComparison.Ordinal)

                if isDirectory then
                    Directory.CreateDirectory destinationPath |> ignore
                    ensureNoReparseParents root destinationPath
                else
                    match Path.GetDirectoryName destinationPath |> Option.ofObj with
                    | None ->
                        raise (InvalidDataException(String.Concat("Browser archive entry has no parent: ", entryName)))
                    | Some parent ->
                        Directory.CreateDirectory parent |> ignore
                        ensureNoReparseParents root parent
                        use source = entry.Open()

                        use destination =
                            new FileStream(
                                destinationPath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                81920,
                                FileOptions.Asynchronous
                            )

                        do! source.CopyToAsync(destination, cancellationToken)
        }

    let ensureUnixExecutable (executablePath: string) =
        if OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() then
            let mode = File.GetUnixFileMode executablePath
            File.SetUnixFileMode(executablePath, mode ||| UnixFileMode.UserExecute)

    let verifyExpectedExecutableAsync
        (browserLock: BrowserLock)
        (platform: BrowserPlatformLock)
        (targetDirectory: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let executable = BrowserManagementInternals.executablePath targetDirectory platform

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
                ensureUnixExecutable executable

                return!
                    BrowserManagementInternals.validateBrowserAsync
                        BrowserOrigin.ManagedCache
                        (Some browserLock.BrowserVersion)
                        executable
                        cancellationToken
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
                BrowserManagementInternals.targetDirectory resolvedCacheRoot browserLock runtimeIdentifier

            let! existingBrowser =
                task {
                    if Directory.Exists targetDirectory then
                        let! validation =
                            verifyExpectedExecutableAsync browserLock platform targetDirectory cancellationToken

                        match validation with
                        | Ok browser -> return Some browser
                        | Error _ ->
                            deleteDirectoryIfPresent targetDirectory
                            return None
                    else
                        return None
                }

            match existingBrowser with
            | Some browser -> return Ok browser
            | None ->
                Directory.CreateDirectory workRoot |> ignore
                let archivePath = Path.Combine(workRoot, "browser.zip")
                let extractionRoot = Path.Combine(workRoot, "extracted")

                do! downloadAndVerifyAsync platform archivePath downloadTimeout cancellationToken
                do! extractArchiveAsync archivePath extractionRoot cancellationToken

                let! staged = verifyExpectedExecutableAsync browserLock platform extractionRoot cancellationToken

                match staged with
                | Error message -> return Error message
                | Ok _ ->
                    match Path.GetDirectoryName targetDirectory |> Option.ofObj with
                    | None -> return Error "Managed browser target directory has no parent."
                    | Some targetParent ->
                        Directory.CreateDirectory targetParent |> ignore
                        Directory.Move(extractionRoot, targetDirectory)

                        let! promoted =
                            verifyExpectedExecutableAsync browserLock platform targetDirectory cancellationToken

                        match promoted with
                        | Ok browser -> return Ok browser
                        | Error message ->
                            deleteDirectoryIfPresent targetDirectory
                            return Error message
        }

module BrowserInstall =
    let locateBrowserLock baseDirectory currentDirectory =
        BrowserManagementInternals.locateBrowserLock baseDirectory currentDirectory

    let installForRuntimeAsync
        (lockPath: string)
        (cacheRoot: string option)
        (runtimeIdentifier: string)
        (downloadTimeout: TimeSpan)
        (cancellationToken: CancellationToken)
        =
        task {
            if
                String.Equals(runtimeIdentifier, "linux-arm64", StringComparison.Ordinal)
                || String.Equals(runtimeIdentifier, "win-arm64", StringComparison.Ordinal)
            then
                return Error(BrowserInstallInternals.unsupportedDiagnostic runtimeIdentifier)
            elif downloadTimeout <= TimeSpan.Zero then
                return Error "Browser download timeout must be positive."
            else
                match BrowserManagementInternals.loadBrowserLock lockPath with
                | Error message -> return Error message
                | Ok browserLock ->
                    match browserLock.Platforms.TryGetValue runtimeIdentifier with
                    | false, _ -> return Error(BrowserInstallInternals.unsupportedDiagnostic runtimeIdentifier)
                    | true, platform ->
                        let cacheRootResult =
                            match cacheRoot with
                            | Some value when not (String.IsNullOrWhiteSpace value) -> Ok(Path.GetFullPath value)
                            | Some _ -> Error "Managed browser cache path must not be empty."
                            | None -> BrowserManagementInternals.cacheRootForRuntime runtimeIdentifier

                        match cacheRootResult with
                        | Error message -> return Error message
                        | Ok resolvedCacheRoot ->
                            let workRoot =
                                Path.Combine(
                                    resolvedCacheRoot,
                                    String.Concat(".install-", runtimeIdentifier, "-", Guid.NewGuid().ToString("N"))
                                )

                            let work =
                                task {
                                    try
                                        Directory.CreateDirectory resolvedCacheRoot |> ignore

                                        let installLockPath =
                                            Path.Combine(
                                                resolvedCacheRoot,
                                                String.Concat(".install-", runtimeIdentifier, ".lock")
                                            )

                                        use! installLock =
                                            BrowserInstallInternals.acquireInstallLockAsync
                                                installLockPath
                                                cancellationToken

                                        return!
                                            BrowserInstallInternals.installUnderLockAsync
                                                browserLock
                                                platform
                                                resolvedCacheRoot
                                                runtimeIdentifier
                                                workRoot
                                                downloadTimeout
                                                cancellationToken
                                    with
                                    | :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                                        return Error "Browser installation was cancelled."
                                    | error ->
                                        return Error(String.Concat("Browser installation failed: ", error.Message))
                                }

                            try
                                return! work
                            finally
                                try
                                    BrowserInstallInternals.deleteDirectoryIfPresent workRoot
                                with _ ->
                                    ()
        }

    let installAsync lockPath cancellationToken =
        installForRuntimeAsync
            lockPath
            None
            (BrowserManagementInternals.currentRuntimeIdentifier ())
            (TimeSpan.FromMinutes 5.0)
            cancellationToken
