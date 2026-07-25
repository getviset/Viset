namespace Viset

open System
open System.Collections.Generic
open System.IO

module internal BrowserDiscovery =
    let private nonEmptyString (value: string | null) =
        value |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let private pathCandidates (executableName: string) =
        match Environment.GetEnvironmentVariable "PATH" |> nonEmptyString with
        | None -> Seq.empty
        | Some pathValue ->
            pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            |> Seq.map (fun directory -> Path.Combine(directory, executableName))

    let systemCandidates (runtimeIdentifier: string) =
        let candidates = ResizeArray<string * string>()
        let seen = HashSet<string> StringComparer.OrdinalIgnoreCase

        let add name path =
            if not (String.IsNullOrWhiteSpace path) then
                let fullPath = Path.GetFullPath path

                if seen.Add fullPath then
                    candidates.Add(name, fullPath)

        let addFromPath name executableName =
            pathCandidates executableName |> Seq.iter (add name)

        let addWindowsRoot variableName name segments =
            match Environment.GetEnvironmentVariable variableName |> nonEmptyString with
            | Some root -> add name (Path.Combine(Array.append [| root |] segments))
            | None -> ()

        if runtimeIdentifier.StartsWith("linux-", StringComparison.Ordinal) then
            addFromPath "Google Chrome" "google-chrome"
            addFromPath "Google Chrome Stable" "google-chrome-stable"
            addFromPath "Chromium" "chromium"
            addFromPath "Chromium Browser" "chromium-browser"
            addFromPath "Microsoft Edge" "microsoft-edge"
            addFromPath "Microsoft Edge Stable" "microsoft-edge-stable"
        elif runtimeIdentifier.StartsWith("osx-", StringComparison.Ordinal) then
            add "Google Chrome" "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

            add
                "Google Chrome for Testing"
                "/Applications/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing"

            add "Chromium" "/Applications/Chromium.app/Contents/MacOS/Chromium"
            add "Microsoft Edge" "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
            addFromPath "Google Chrome" "google-chrome"
            addFromPath "Chromium" "chromium"
        elif runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal) then
            addWindowsRoot "PROGRAMFILES" "Google Chrome" [| "Google"; "Chrome"; "Application"; "chrome.exe" |]

            addWindowsRoot "PROGRAMFILES(X86)" "Google Chrome" [| "Google"; "Chrome"; "Application"; "chrome.exe" |]

            addWindowsRoot "LOCALAPPDATA" "Google Chrome" [| "Google"; "Chrome"; "Application"; "chrome.exe" |]

            addWindowsRoot "PROGRAMFILES" "Chromium" [| "Chromium"; "Application"; "chrome.exe" |]

            addWindowsRoot "PROGRAMFILES(X86)" "Microsoft Edge" [| "Microsoft"; "Edge"; "Application"; "msedge.exe" |]

            addWindowsRoot "PROGRAMFILES" "Microsoft Edge" [| "Microsoft"; "Edge"; "Application"; "msedge.exe" |]

            addFromPath "Google Chrome" "chrome.exe"
            addFromPath "Chromium" "chromium.exe"
            addFromPath "Microsoft Edge" "msedge.exe"

        candidates |> Seq.filter (fun (_, path) -> File.Exists path) |> Seq.toList
