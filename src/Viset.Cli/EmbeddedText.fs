namespace Viset

open System
open System.IO
open System.Text

module internal EmbeddedText =
    let read<'Marker> (resourceName: string) =
        let assembly = typeof<'Marker>.Assembly

        use stream =
            assembly.GetManifestResourceStream(resourceName)
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                let available =
                    assembly.GetManifestResourceNames() |> String.concat Environment.NewLine

                invalidOp
                    $"""Embedded resource '{resourceName}' was not found in '{assembly.FullName}'.

                    Available resources:
                    {available}
                    """)

        use reader = new StreamReader(stream, Encoding.UTF8)
        reader.ReadToEnd()
