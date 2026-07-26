namespace Viset

open System
open System.IO
open System.Threading

module BrowserResolution =
    let private tryManagedAsync
        (lockPath: string option)
        (cacheRoot: string option)
        (runtimeIdentifier: string)
        (cancellationToken: CancellationToken)
        =
        task {
            match BrowserLockContract.loadTrustedBrowserLock lockPath with
            | Error message -> return Error message
            | Ok browserLock ->
                match browserLock.Platforms.TryGetValue runtimeIdentifier with
                | false, _ -> return Ok None
                | true, platform ->
                    let rootResult =
                        match cacheRoot with
                        | Some root when not (String.IsNullOrWhiteSpace root) ->
                            Ok(Path.GetFullPath root)
                        | Some _ -> Error "Managed browser cache path must not be empty."
                        | None -> BrowserCache.cacheRootForRuntime runtimeIdentifier

                    match rootResult with
                    | Error _ -> return Ok None
                    | Ok root ->
                        let target = BrowserCache.targetDirectory root browserLock runtimeIdentifier

                        let executable = BrowserCache.executablePath target platform

                        if not (File.Exists executable) then
                            return Ok None
                        else
                            let! validation =
                                BrowserValidation.validateManagedBrowserAsync
                                    browserLock.BrowserVersion
                                    executable
                                    cancellationToken

                            match validation with
                            | Ok browser -> return Ok(Some browser)
                            | Error _ -> return Ok None
        }

    let rec private trySystemAsync candidates cancellationToken =
        task {
            match candidates with
            | [] -> return None
            | (name, path) :: remaining ->
                let! result =
                    BrowserValidation.validateBrowserAsync
                        (SystemDiscovery name)
                        None
                        path
                        cancellationToken

                match result with
                | Ok browser -> return Some browser
                | Error _ -> return! trySystemAsync remaining cancellationToken
        }

    let resolveForRuntimeAsync
        (explicitPath: string option)
        (lockPath: string option)
        (cacheRoot: string option)
        (runtimeIdentifier: string)
        (cancellationToken: CancellationToken)
        =
        task {
            match explicitPath with
            | Some path ->
                return!
                    BrowserValidation.validateBrowserAsync ExplicitPath None path cancellationToken
            | None ->
                match Environment.GetEnvironmentVariable "VISET_BROWSER" |> Option.ofObj with
                | Some path when not (String.IsNullOrWhiteSpace path) ->
                    return!
                        BrowserValidation.validateBrowserAsync
                            EnvironmentVariable
                            None
                            path
                            cancellationToken
                | _ ->
                    let! managedResult =
                        tryManagedAsync lockPath cacheRoot runtimeIdentifier cancellationToken

                    match managedResult with
                    | Error message -> return Error message
                    | Ok(Some browser) -> return Ok browser
                    | Ok None ->
                        let! systemBrowser =
                            trySystemAsync
                                (BrowserDiscovery.systemCandidates runtimeIdentifier)
                                cancellationToken

                        match systemBrowser with
                        | Some browser -> return Ok browser
                        | None ->
                            return
                                Error
                                    "No usable browser was found. Set --browser, VISET_BROWSER, run 'viset browser install', or install Chrome, Chromium, or Edge."
        }

    let resolveAsync explicitPath lockPath cancellationToken =
        resolveForRuntimeAsync
            explicitPath
            lockPath
            None
            (BrowserRuntime.currentRuntimeIdentifier ())
            cancellationToken
