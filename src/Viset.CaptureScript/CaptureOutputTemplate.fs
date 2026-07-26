namespace Viset

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open Tomlyn.Model

module internal CaptureOutputTemplate =
    open CaptureScriptPrimitives

    let parseAxes (matrix: TomlTable) =
        matrix
        |> Seq.map (fun entry -> entry.Key, entry.Value)
        |> List.ofSeq
        |> traverse (fun _ (axisName, axisValue) ->
            if String.IsNullOrWhiteSpace axisName then
                error "matrix contains an empty axis name."
            elif String.Equals(axisName, "device", StringComparison.Ordinal) then
                error
                    "matrix.device is redundant; declared devices expand automatically in declaration order."
            else
                match axisValue with
                | :? TomlArray as values when values.Count > 0 ->
                    values
                    |> Seq.cast<obj | null>
                    |> List.ofSeq
                    |> traverse (fun index value ->
                        TomlValueParser.parse
                            (concat [| "matrix."; axisName; "["; invariantInt32 index; "]" |])
                            value)
                    |> Result.map (fun parsed -> axisName, parsed)
                | :? TomlArray -> error (concat [| "matrix."; axisName; " must not be empty." |])
                | _ -> error (concat [| "matrix."; axisName; " must be a TOML array." |]))

    let private scalarText placeholder value =
        match value with
        | String text -> Ok text
        | Integer number -> Ok(number.ToString CultureInfo.InvariantCulture)
        | Float number -> Ok(number.ToString("R", CultureInfo.InvariantCulture))
        | Boolean flag -> Ok(if flag then "true" else "false")
        | DateTime dateTime -> Ok dateTime
        | Array _
        | Table _ ->
            error (
                concat
                    [| "Output placeholder '{"
                       placeholder
                       "}' refers to a non-scalar matrix value." |]
            )

    let render (template: string) (values: (string * TomlValue) list) =
        let bindings = Dictionary<string, TomlValue> StringComparer.Ordinal
        values |> List.iter (fun (name, value) -> bindings.Add(name, value))
        let rendered = StringBuilder()

        let rec append index =
            if index >= template.Length then
                Ok(rendered.ToString())
            else
                match template[index] with
                | '}' -> error "output contains an unmatched closing brace."
                | '{' ->
                    let closingIndex = template.IndexOf('}', index + 1)

                    if closingIndex < 0 then
                        error "output contains an unmatched opening brace."
                    else
                        let placeholder = template.Substring(index + 1, closingIndex - index - 1)

                        if String.IsNullOrWhiteSpace placeholder || placeholder.Contains '{' then
                            error "output contains an invalid placeholder."
                        else
                            match bindings.TryGetValue placeholder with
                            | false, _ ->
                                error (
                                    concat
                                        [| "output requires missing capture value '"
                                           placeholder
                                           "'." |]
                                )
                            | true, value ->
                                match scalarText placeholder value with
                                | Error errors -> Error errors
                                | Ok text ->
                                    rendered.Append text |> ignore
                                    append (closingIndex + 1)
                | character ->
                    rendered.Append character |> ignore
                    append (index + 1)

        append 0

    let validateRelativePath (value: string) =
        let invalidCharacters =
            [| '<'; '>'; ':'; '"'; '|'; '?'; '*'; '#'; '!'; '['; ']'; '{'; '}' |]

        if String.IsNullOrWhiteSpace value then
            error "output expands to an empty path."
        elif Path.IsPathRooted value || value.Contains '\\' then
            error (String.Concat("output expands to unsafe path '", value, "'."))
        elif value.IndexOfAny invalidCharacters >= 0 || value |> Seq.exists Char.IsControl then
            error (String.Concat("output expands to unsafe path '", value, "'."))
        else
            let segments = value.Split('/', StringSplitOptions.None)

            if
                segments
                |> Array.exists (fun segment ->
                    String.IsNullOrEmpty segment
                    || segment = "."
                    || segment = ".."
                    || segment.StartsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith ' '
                    || segment.EndsWith '.')
            then
                error (String.Concat("output expands to unsafe path '", value, "'."))
            else
                Ok value

    let expandAxes (axes: (string * TomlValue list) list) =
        axes
        |> List.fold
            (fun combinations (axisName, values) ->
                [ for combination in combinations do
                      for value in values do
                          yield combination @ [ axisName, value ] ])
            [ [] ]
