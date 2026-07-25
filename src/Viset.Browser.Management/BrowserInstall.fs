namespace Viset

open System
open System.IO
open System.Threading

module BrowserInstall =
    let findBrowserLockSidecar baseDirectory =
        BrowserLockContract.locateBrowserLockSidecar baseDirectory

    let validateBrowserLockSidecar lockPath =
        BrowserLockContract.validateBrowserLockSidecar lockPath

    let installForRuntimeAsync
        (lockPath: string option)
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
                return Error(BrowserInstaller.unsupportedDiagnostic runtimeIdentifier)
            elif downloadTimeout <= TimeSpan.Zero then
                return Error "Browser download timeout must be positive."
            else
                match BrowserLockContract.loadTrustedBrowserLock lockPath with
                | Error message -> return Error message
                | Ok browserLock ->
                    match browserLock.Platforms.TryGetValue runtimeIdentifier with
                    | false, _ -> return Error(BrowserInstaller.unsupportedDiagnostic runtimeIdentifier)
                    | true, platform ->
                        let cacheRootResult =
                            match cacheRoot with
                            | Some value when not (String.IsNullOrWhiteSpace value) -> Ok(Path.GetFullPath value)
                            | Some _ -> Error "Managed browser cache path must not be empty."
                            | None -> BrowserCache.cacheRootForRuntime runtimeIdentifier

                        match cacheRootResult with
                        | Error message -> return Error message
                        | Ok resolvedCacheRoot ->
                            let workRoot =
                                Path.Combine(
                                    resolvedCacheRoot,
                                    String.Concat(".install-", runtimeIdentifier, "-", Guid.NewGuid().ToString "N")
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

                                        use! _ =
                                            BrowserInstaller.acquireInstallLockAsync installLockPath cancellationToken

                                        return!
                                            BrowserInstaller.installUnderLockAsync
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
                                    BrowserInstaller.deleteDirectoryIfPresent workRoot
                                with _ ->
                                    ()
        }

    let installAsync lockPath cancellationToken =
        installForRuntimeAsync
            lockPath
            None
            (BrowserRuntime.currentRuntimeIdentifier ())
            (TimeSpan.FromMinutes 5.0)
            cancellationToken
