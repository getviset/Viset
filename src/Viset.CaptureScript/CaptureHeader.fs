namespace Viset

open System
open System.IO

module internal CaptureHeader =
    open CaptureScriptPrimitives

    let extract (source: string) =
        let mutable index = 0

        while index < source.Length
              && (Char.IsWhiteSpace source[index] || source[index] = '\uFEFF') do
            index <- index + 1

        if
            index + 4 > source.Length
            || not (source.AsSpan(index, 4).SequenceEqual("--[[".AsSpan()))
        then
            error "Capture Lua must begin with a --[[ TOML header block."
        else
            let contentStart = index + 4
            let closingIndex = source.IndexOf("]]", contentStart, StringComparison.Ordinal)

            if closingIndex < 0 then
                error "Capture Lua TOML header block is not closed with ]]."
            else
                let content = source.Substring(contentStart, closingIndex - contentStart)
                let trimmed = content.TrimStart [| '\r'; '\n'; ' '; '\t' |]

                use reader = new StringReader(trimmed)

                match reader.ReadLine() |> Option.ofObj with
                | Some marker when String.Equals(marker.Trim(), "# viset", StringComparison.Ordinal) ->
                    Ok content
                | _ -> error "Capture Lua TOML header must begin with '# viset'."
