namespace Viset

open System
open System.Globalization
open System.Text

module private ScaffoldTemplate =
    let render (values: Map<string, string>) (template: string) =
        let output = StringBuilder(template.Length)

        let mutable position = 0

        while position < template.Length do
            let opening = template.IndexOf("{{", position, StringComparison.Ordinal)

            if opening < 0 then
                output.Append(template, position, template.Length - position) |> ignore

                position <- template.Length
            else
                output.Append(template, position, opening - position) |> ignore
                let closing = template.IndexOf("}}", opening + 2, StringComparison.Ordinal)

                if closing < 0 then
                    invalidOp "Scaffold template contains an unterminated placeholder."

                let name = template.Substring(opening + 2, closing - opening - 2)

                match Map.tryFind name values with
                | Some value -> output.Append(value) |> ignore
                | None -> invalidOp $"Unknown scaffold template placeholder '{{{{{name}}}}}'."

                position <- closing + 2

        output.ToString()

module internal ScaffoldContent =
    let private escapeTomlString (value: string) =
        let escaped = StringBuilder(value.Length)

        value
        |> Seq.iter (fun character ->
            match character with
            | '"' -> escaped.Append("\\\"") |> ignore
            | '\\' -> escaped.Append("\\\\") |> ignore
            | '\b' -> escaped.Append("\\b") |> ignore
            | '\t' -> escaped.Append("\\t") |> ignore
            | '\n' -> escaped.Append("\\n") |> ignore
            | '\f' -> escaped.Append("\\f") |> ignore
            | '\r' -> escaped.Append("\\r") |> ignore
            | character when Char.IsControl(character) ->
                escaped.Append("\\u") |> ignore

                escaped.Append((int character).ToString("X4", CultureInfo.InvariantCulture))
                |> ignore
            | character -> escaped.Append(character) |> ignore)

        escaped.ToString()

    let capture (settings: ScaffoldSettings) =
        ScaffoldAssets.captureTemplate
        |> ScaffoldTemplate.render (
            Map.ofList
                [ "OUTPUT_PATH", escapeTomlString settings.OutputPath
                  "PAGE_URL", escapeTomlString settings.PageUrl
                  "VIEWPORT_WIDTH", settings.ViewportWidth.ToString(CultureInfo.InvariantCulture)
                  "VIEWPORT_HEIGHT", settings.ViewportHeight.ToString(CultureInfo.InvariantCulture) ]
        )

    let readme (settings: ScaffoldSettings) =
        ScaffoldAssets.readmeTemplate
        |> ScaffoldTemplate.render (Map.ofList [ "OUTPUT_PATH", settings.OutputPath ])

    let gitignore (settings: ScaffoldSettings) =
        let segments = settings.OutputPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
        let suffix = if segments.Length = 1 then String.Empty else "/"

        String.Concat("/", segments[0], suffix, "\n")
