namespace Viset

open System
open System.Diagnostics
open System.Globalization

[<DebuggerDisplay("TomlValue")>]
type TomlValue =
    | String of string
    | Integer of int64
    | Float of double
    | Boolean of bool
    | DateTime of string
    | Array of TomlValue list
    | Table of (string * TomlValue) list

    override value.ToString() =
        match value with
        | String text -> text
        | Integer number -> number.ToString(CultureInfo.InvariantCulture)
        | Float number -> number.ToString("R", CultureInfo.InvariantCulture)
        | Boolean flag -> if flag then "true" else "false"
        | DateTime text -> text
        | Array _ -> "array"
        | Table _ -> "table"
