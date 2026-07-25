namespace Viset

open System
open System.IO
open System.Runtime.InteropServices

module internal WebPNativeLibraries =
    type private NativeLibraries =
        { SharpYuv: nativeint
          WebP: nativeint
          WebPMux: nativeint }

    let private nativeFileName library =
        if OperatingSystem.IsWindows() then
            String.Concat(library, ".dll")
        elif OperatingSystem.IsMacOS() then
            String.Concat(library, ".dylib")
        else
            String.Concat(library, ".so")

    let private loadNativeLibrary library =
        let fileName = nativeFileName library

        let candidates =
            [ Path.Combine(AppContext.BaseDirectory, fileName)
              Path.Combine(
                  AppContext.BaseDirectory,
                  "runtimes",
                  RuntimeInformation.RuntimeIdentifier,
                  "native",
                  fileName
              ) ]

        match candidates |> List.tryFind File.Exists with
        | Some path -> NativeLibrary.Load path
        | None ->
            invalidOp (
                String.Concat(
                    "The packaged libwebp sidecar '",
                    fileName,
                    "' was not found. Looked in: ",
                    String.Join(", ", candidates)
                )
            )

    let private nativeLibraries =
        lazy
            (let libraries =
                { SharpYuv = loadNativeLibrary "libsharpyuv"
                  WebP = loadNativeLibrary "libwebp"
                  WebPMux = loadNativeLibrary "libwebpmux" }

             let resolver =
                 DllImportResolver(fun libraryName _ _ ->
                     match libraryName with
                     | "libwebp" -> libraries.WebP
                     | "libwebpmux" -> libraries.WebPMux
                     | _ -> 0n)

             NativeLibrary.SetDllImportResolver(typeof<WebPConfig>.Assembly, resolver)
             libraries)

    let ensureLoaded () = nativeLibraries.Value |> ignore

    let webPMemoryWriter () =
        NativeLibrary.GetExport(nativeLibraries.Value.WebP, "WebPMemoryWrite")
