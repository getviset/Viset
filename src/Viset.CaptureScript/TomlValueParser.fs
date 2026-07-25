namespace Viset

open System
open Tomlyn.Model

module internal TomlValueParser =
    open CaptureScriptPrimitives
    open Tomlyn

    [<Literal>]
    let private MaximumLuaSafeInteger = 9007199254740991L

    let rec parse path (value: obj | null) =
        let parseArray (values: (obj | null) list) =
            values
            |> traverse (fun index item -> parse (appendIndex path index) item)
            |> Result.map TomlValue.Array

        let parseTable (table: TomlTable) =
            table
            |> Seq.map (fun entry -> entry.Key, entry.Value)
            |> List.ofSeq
            |> traverse (fun _ (key, item) -> parse (appendKey path key) item |> Result.map (fun parsed -> key, parsed))
            |> Result.map Table

        match value with
        | null -> error (String.Concat(path, " contains an unsupported null value."))
        | :? string as text -> Ok(TomlValue.String text)
        | :? bool as flag -> Ok(TomlValue.Boolean flag)
        | :? int64 as number ->
            if number < -MaximumLuaSafeInteger || number > MaximumLuaSafeInteger then
                error (
                    concat
                        [| path
                           " contains integer "
                           invariantInt64 number
                           ", outside Lua's safe integer range." |]
                )
            else
                Ok(Integer number)
        | :? double as number ->
            if Double.IsFinite number then
                Ok(Float number)
            else
                error (String.Concat(path, " contains a non-finite number."))
        | :? TomlDateTime as dateTime -> Ok(TomlValue.DateTime(dateTime.ToString()))
        | :? TomlTable as table -> parseTable table
        | :? TomlTableArray as tables ->
            tables
            |> Seq.cast<TomlTable>
            |> Seq.map (fun table -> table :> obj)
            |> List.ofSeq
            |> parseArray
        | :? TomlArray as values -> values |> Seq.cast<obj | null> |> List.ofSeq |> parseArray
        | _ -> error (String.Concat(path, " contains an unsupported TOML value."))

    let parseTable path (table: TomlTable) =
        table
        |> Seq.map (fun entry -> entry.Key, entry.Value)
        |> List.ofSeq
        |> traverse (fun _ (key, value) -> parse (appendKey path key) value |> Result.map (fun parsed -> key, parsed))
