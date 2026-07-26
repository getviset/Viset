namespace Viset

open System.IO

type private LuaBootstrapResourceMarker = class end

module internal LuaBootstrap =
    [<Literal>]
    let private ResourceName = "Viset.LuaBootstrap.lua"

    let source =
        let assembly = typeof<LuaBootstrapResourceMarker>.Assembly

        use stream =
            assembly.GetManifestResourceStream ResourceName
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                invalidOp $"Embedded resource '{ResourceName}' was not found.")

        use reader = new StreamReader(stream)
        reader.ReadToEnd()
