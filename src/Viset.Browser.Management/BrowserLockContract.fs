namespace Viset

open System
open System.Collections.Generic
open System.IO
open Viset.Serialization

module internal BrowserLockContract =
    let browserLockFileName = "browser-lock.toml"
    let private expectedPublisher = "Google Chrome for Testing"
    let private supportedPlatforms = [| "linux-x64"; "win-x64"; "osx-arm64" |]

    let private canonicalPlatform runtimeIdentifier url sha256 executableLayout =
        { RuntimeIdentifier = runtimeIdentifier
          Url = Uri url
          Sha256 = sha256
          ExecutableLayout = executableLayout }

    let canonicalBrowserLock =
        let platforms = Dictionary<string, BrowserPlatformLock> StringComparer.Ordinal

        platforms.Add(
            "linux-x64",
            canonicalPlatform
                "linux-x64"
                "https://storage.googleapis.com/chrome-for-testing-public/150.0.7871.124/linux64/chrome-linux64.zip"
                "ccb11556d5946fcf15f09d175c34d8e4b4293a8ef2eb7c4efc28cb60ac4d12fd"
                "chrome-linux64/chrome"
        )

        platforms.Add(
            "win-x64",
            canonicalPlatform
                "win-x64"
                "https://storage.googleapis.com/chrome-for-testing-public/150.0.7871.124/win64/chrome-win64.zip"
                "65fff74a602e0487fac43a148229b56d859070ac1b0e3d002d21d8edae3cfafe"
                "chrome-win64/chrome.exe"
        )

        platforms.Add(
            "osx-arm64",
            canonicalPlatform
                "osx-arm64"
                "https://storage.googleapis.com/chrome-for-testing-public/150.0.7871.124/mac-arm64/chrome-mac-arm64.zip"
                "36c8b5fe04c08a418a172206bb392600ec1550941bde6af2d4353df21db87a47"
                "chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing"
        )

        { LockPath = "compiled browser contract"
          BrowserVersion = "150.0.7871.124"
          Revision = "r1639810"
          Platforms = platforms }

    let private nonEmptyString (value: string | null) =
        value |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let private requireText label (value: string | null) =
        match nonEmptyString value with
        | Some text -> text
        | None ->
            raise (InvalidDataException(String.Concat("browser-lock.toml requires ", label, ".")))

    let private validateSha256 runtimeIdentifier value =
        let digest =
            requireText (String.Concat("platforms.", runtimeIdentifier, ".sha256")) value

        if digest.Length <> 64 || not (digest |> Seq.forall Char.IsAsciiHexDigit) then
            raise (
                InvalidDataException(
                    String.Concat(
                        "browser-lock.toml has an invalid SHA-256 for ",
                        runtimeIdentifier,
                        "."
                    )
                )
            )

        digest.ToLowerInvariant()

    let private validateExecutableLayout runtimeIdentifier value =
        let layout =
            requireText (String.Concat("platforms.", runtimeIdentifier, ".executable")) value

        let hasUnsafeSegment =
            layout.Split('/', StringSplitOptions.None)
            |> Array.exists (fun segment ->
                String.IsNullOrWhiteSpace segment
                || String.Equals(segment, ".", StringComparison.Ordinal)
                || String.Equals(segment, "..", StringComparison.Ordinal)
                || segment.Contains ':')

        if
            layout.Contains '\\'
            || layout.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted layout
            || hasUnsafeSegment
        then
            raise (
                InvalidDataException(
                    String.Concat(
                        "browser-lock.toml has an unsafe executable layout for ",
                        runtimeIdentifier,
                        "."
                    )
                )
            )

        layout

    let private validatePlatform runtimeIdentifier (model: BrowserLockPlatformTomlModel) =
        let urlText =
            requireText (String.Concat("platforms.", runtimeIdentifier, ".url")) model.Url

        let url =
            match Uri.TryCreate(urlText, UriKind.Absolute) with
            | true, parsed ->
                match Option.ofObj parsed with
                | Some value when
                    value.Scheme = Uri.UriSchemeHttps
                    && String.Equals(value.Host, "storage.googleapis.com", StringComparison.Ordinal)
                    && value.IsDefaultPort
                    ->
                    value
                | _ ->
                    raise (
                        InvalidDataException(
                            String.Concat(
                                "browser-lock.toml has an invalid download URL for ",
                                runtimeIdentifier,
                                "."
                            )
                        )
                    )
            | false, _ ->
                raise (
                    InvalidDataException(
                        String.Concat(
                            "browser-lock.toml has an invalid download URL for ",
                            runtimeIdentifier,
                            "."
                        )
                    )
                )

        { RuntimeIdentifier = runtimeIdentifier
          Url = url
          Sha256 = validateSha256 runtimeIdentifier model.Sha256
          ExecutableLayout = validateExecutableLayout runtimeIdentifier model.Executable }

    let private parseBrowserLock (lockPath: string) =
        try
            if String.IsNullOrWhiteSpace lockPath then
                Error "browser-lock.toml path must not be empty."
            elif not (File.Exists lockPath) then
                Error(String.Concat("browser-lock.toml was not found: ", lockPath))
            else
                let fullPath = Path.GetFullPath lockPath
                let model = File.ReadAllText fullPath |> BrowserLockToml.Deserialize

                if model.Version <> Nullable 1L then
                    raise (InvalidDataException "browser-lock.toml version must be 1.")

                let publisher = requireText "publisher" model.Publisher

                if not (String.Equals(publisher, expectedPublisher, StringComparison.Ordinal)) then
                    raise (
                        InvalidDataException(
                            String.Concat(
                                "browser-lock.toml publisher must be '",
                                expectedPublisher,
                                "'."
                            )
                        )
                    )

                let browserVersion = requireText "browser_version" model.BrowserVersion
                let revision = requireText "revision" model.Revision

                if model.Platforms.Count <> supportedPlatforms.Length then
                    raise (
                        InvalidDataException
                            "browser-lock.toml must define exactly linux-x64, win-x64, and osx-arm64."
                    )

                let platforms = Dictionary<string, BrowserPlatformLock> StringComparer.Ordinal

                for runtimeIdentifier in supportedPlatforms do
                    match model.Platforms.TryGetValue runtimeIdentifier with
                    | true, platform ->
                        platforms.Add(
                            runtimeIdentifier,
                            validatePlatform runtimeIdentifier platform
                        )
                    | false, _ ->
                        raise (
                            InvalidDataException(
                                String.Concat(
                                    "browser-lock.toml is missing platforms.",
                                    runtimeIdentifier,
                                    "."
                                )
                            )
                        )

                for runtimeIdentifier in model.Platforms.Keys do
                    if not (platforms.ContainsKey runtimeIdentifier) then
                        raise (
                            InvalidDataException(
                                String.Concat(
                                    "browser-lock.toml contains unsupported platform '",
                                    runtimeIdentifier,
                                    "'."
                                )
                            )
                        )

                Ok
                    { LockPath = fullPath
                      BrowserVersion = browserVersion
                      Revision = revision
                      Platforms = platforms }
        with error ->
            Error(String.Concat("Failed to read browser-lock.toml: ", error.Message))

    let private browserLocksEqual (candidate: BrowserLock) =
        let canonical = canonicalBrowserLock

        String.Equals(candidate.BrowserVersion, canonical.BrowserVersion, StringComparison.Ordinal)
        && String.Equals(candidate.Revision, canonical.Revision, StringComparison.Ordinal)
        && candidate.Platforms.Count = canonical.Platforms.Count
        && supportedPlatforms
           |> Array.forall (fun runtimeIdentifier ->
               match
                   candidate.Platforms.TryGetValue runtimeIdentifier,
                   canonical.Platforms.TryGetValue runtimeIdentifier
               with
               | (true, candidatePlatform), (true, canonicalPlatform) ->
                   candidatePlatform.Url = canonicalPlatform.Url
                   && String.Equals(
                       candidatePlatform.Sha256,
                       canonicalPlatform.Sha256,
                       StringComparison.Ordinal
                   )
                   && String.Equals(
                       candidatePlatform.ExecutableLayout,
                       canonicalPlatform.ExecutableLayout,
                       StringComparison.Ordinal
                   )
               | _ -> false)

    let loadTrustedBrowserLock (lockPath: string option) =
        match lockPath with
        | None -> Ok canonicalBrowserLock
        | Some path ->
            match parseBrowserLock path with
            | Ok candidate when browserLocksEqual candidate -> Ok candidate
            | Ok _ ->
                Error "browser-lock.toml does not match the compiled trusted browser contract."
            | Error message -> Error message

    let validateBrowserLockSidecar lockPath =
        loadTrustedBrowserLock (Some lockPath) |> Result.map ignore

    let locateBrowserLockSidecar (baseDirectory: string) =
        let basePath = Path.Combine(baseDirectory, browserLockFileName)

        if File.Exists basePath then
            Some(Path.GetFullPath basePath)
        else
            None
