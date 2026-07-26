namespace Viset

open System
open System.IO

module internal OutputSafety =
    let private pathComparison =
        if OperatingSystem.IsWindows() then
            StringComparison.OrdinalIgnoreCase
        else
            StringComparison.Ordinal

    let private entryExists path =
        File.Exists path || Directory.Exists path

    let private isLinkOrReparse (info: FileSystemInfo) =
        info.LinkTarget |> Option.ofObj |> Option.isSome
        || info.Exists && info.Attributes.HasFlag FileAttributes.ReparsePoint

    let ensureExistingAncestorsAreNotLinked path =
        let mutable current = Some(Path.GetFullPath path)

        while current.IsSome do
            let candidate = current.Value

            if Directory.Exists candidate then
                let directory = DirectoryInfo candidate

                if isLinkOrReparse directory then
                    raise (
                        InvalidDataException(
                            String.Concat("Output path is a link or reparse point: ", candidate)
                        )
                    )

            current <- Path.GetDirectoryName candidate |> Option.ofObj

    let ensureTargetIsSafe path =
        let target = FileInfo path

        if isLinkOrReparse target then
            raise (
                InvalidDataException(
                    String.Concat("Output file is a link or reparse point: ", path)
                )
            )

        if Directory.Exists path then
            raise (
                InvalidDataException(
                    String.Concat("Output file path is occupied by a directory: ", path)
                )
            )

        Path.GetDirectoryName path
        |> Option.ofObj
        |> Option.iter ensureExistingAncestorsAreNotLinked

    let private ensureContained root path =
        let normalizedRoot = Path.GetFullPath root
        let normalizedPath = Path.GetFullPath path

        if String.Equals(normalizedRoot, normalizedPath, pathComparison) then
            raise (InvalidDataException "Output file path must not equal the output root.")

        let prefix =
            String.Concat(
                normalizedRoot.TrimEnd Path.DirectorySeparatorChar,
                Path.DirectorySeparatorChar
            )

        if not (normalizedPath.StartsWith(prefix, pathComparison)) then
            raise (
                InvalidDataException(
                    String.Concat("Output path escapes the output root: ", normalizedPath)
                )
            )

    let preflight (plan: CapturePlan) =
        let root = Path.GetFullPath plan.OutputPath

        if File.Exists root then
            raise (InvalidDataException(String.Concat("Output root is a file: ", root)))

        ensureExistingAncestorsAreNotLinked root

        for capture in plan.Captures do
            ensureContained root capture.OutputPath
            ensureTargetIsSafe capture.OutputPath

            if entryExists capture.OutputPath && not plan.Force then
                raise (
                    IOException(
                        String.Concat(
                            "Refusing to overwrite existing output without --force: ",
                            capture.OutputPath
                        )
                    )
                )
