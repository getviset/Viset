namespace Viset

open System
open System.IO

module internal BrowserCache =
    let private nonEmptyString (value: string | null) =
        value |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let cacheRootForRuntime (runtimeIdentifier: string) =
        let environmentRoot variableName segments =
            match Environment.GetEnvironmentVariable variableName |> nonEmptyString with
            | Some root -> Ok(Path.Combine(Array.append [| root |] segments))
            | None -> Error(String.Concat(variableName, " is not set; the managed browser cache cannot be located."))

        if runtimeIdentifier.StartsWith("linux-", StringComparison.Ordinal) then
            match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" |> nonEmptyString with
            | Some root -> Ok(Path.Combine(root, "viset", "browser"))
            | None -> environmentRoot "HOME" [| ".cache"; "viset"; "browser" |]
        elif runtimeIdentifier.StartsWith("osx-", StringComparison.Ordinal) then
            environmentRoot "HOME" [| "Library"; "Caches"; "Viset"; "browser" |]
        elif runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) then
            environmentRoot "LOCALAPPDATA" [| "Viset"; "browser" |]
        else
            Error(String.Concat("No managed browser cache is defined for ", runtimeIdentifier, "."))

    let targetDirectory (cacheRoot: string) (browserLock: BrowserLock) (runtimeIdentifier: string) =
        Path.Combine(cacheRoot, String.Concat(browserLock.BrowserVersion, "-", browserLock.Revision), runtimeIdentifier)

    let executablePath (targetDirectoryPath: string) (platform: BrowserPlatformLock) =
        platform.ExecutableLayout.Split('/', StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold (fun current segment -> Path.Combine(current, segment)) targetDirectoryPath
