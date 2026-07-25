namespace Viset

open System
open System.Diagnostics
open System.Globalization

[<DebuggerDisplay("BuiltInFrameStyle")>]
type BuiltInFrameStyle =
    | Automatic
    | Phone
    | Laptop

    override style.ToString() =
        match style with
        | Automatic -> "auto"
        | Phone -> "phone"
        | Laptop -> "laptop"

[<DebuggerDisplay("FrameSource")>]
type FrameSource =
    | CustomFrame of path: string
    | BuiltInFrame of style: BuiltInFrameStyle

    override source.ToString() =
        match source with
        | CustomFrame path -> path
        | BuiltInFrame style -> String.Concat("builtin:", style.ToString())
