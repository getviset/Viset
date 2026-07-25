namespace Viset

open System
open System.IO
open System.Text

module internal ScaffoldFileSystem =
    let private generatedFileNames =
        [ "capture.lua"
          "README.md"
          ".gitignore"
          ".luarc.json"
          Path.Combine(".viset", "viset.d.lua") ]

    let private generatedDirectoryNames = [ ".viset" ]

    let entryExists path =
        if File.Exists(path) || Directory.Exists(path) then
            true
        else
            try
                File.GetAttributes(path) |> ignore
                true
            with
            | :? FileNotFoundException
            | :? DirectoryNotFoundException -> false

    let isLink path =
        entryExists path
        && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)

    let generatedPaths directory =
        generatedFileNames
        |> List.map (fun fileName -> Path.Combine(directory, fileName))

    let generatedDirectoryPaths directory =
        generatedDirectoryNames |> List.map (fun path -> Path.Combine(directory, path))

    let private deleteLink path =
        if isLink path then
            File.Delete(path)

    let writeFile (path: string) (content: string) =
        deleteLink path

        Path.GetDirectoryName(path)
        |> Option.ofObj
        |> Option.iter (fun directory -> Directory.CreateDirectory(directory) |> ignore)

        File.WriteAllText(path, content, UTF8Encoding(false))
