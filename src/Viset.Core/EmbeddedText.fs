namespace Viset

open System.IO

module EmbeddedText =
    let read<'Marker> resourceName =
        let assembly = typeof<'Marker>.Assembly

        use stream =
            assembly.GetManifestResourceStream resourceName
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                invalidOp
                    $"Embedded resource '{resourceName}' was not found in '{assembly.FullName}'")

        use reader = new StreamReader(stream)
        reader.ReadToEnd()
