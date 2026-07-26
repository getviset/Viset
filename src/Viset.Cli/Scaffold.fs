namespace Viset

open System
open System.IO

type ScaffoldResult =
    { DirectoryPath: string
      CapturePath: string }

    override result.ToString() = result.DirectoryPath

module Scaffold =
    let run request =
        try
            match ScaffoldValidation.validateTarget request with
            | Error message -> Error message
            | Ok() ->
                match ScaffoldInput.settings request.Interactive with
                | Error message -> Error message
                | Ok settings ->
                    Directory.CreateDirectory(request.TargetDirectory) |> ignore

                    let write relativePath content =
                        ScaffoldFileSystem.writeFile
                            (Path.Combine(request.TargetDirectory, relativePath))
                            content

                    write "capture.lua" (ScaffoldContent.capture settings)
                    write "README.md" (ScaffoldContent.readme settings)
                    write ".gitignore" (ScaffoldContent.gitignore settings)
                    write ".luarc.json" EditorSupport.LuaLanguageServerConfiguration
                    write (Path.Combine(".viset", "viset.d.lua")) EditorSupport.LuaDefinitions

                    Ok
                        { DirectoryPath = request.TargetDirectory
                          CapturePath = Path.Combine(request.TargetDirectory, "capture.lua") }

        with error ->
            Error $"Project initialization failed: {error.Message}"
