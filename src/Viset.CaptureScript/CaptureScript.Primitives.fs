namespace Viset

open System
open System.Globalization

module internal CaptureScriptPrimitives =
    let error message = Error [ message ]

    let concat (parts: string array) = String.Concat parts

    let invariantInt32 (value: int) =
        value.ToString CultureInfo.InvariantCulture

    let invariantInt64 (value: int64) =
        value.ToString CultureInfo.InvariantCulture

    let appendIndex path index =
        concat [| path; "["; invariantInt32 index; "]" |]

    let appendKey path key = concat [| path; "."; key |]

    let traverse parser values =
        let rec loop index parsed remaining =
            match remaining with
            | [] -> Ok(List.rev parsed)
            | value :: tail ->
                match parser index value with
                | Ok parsedValue -> loop (index + 1) (parsedValue :: parsed) tail
                | Error errors -> Error errors

        loop 0 [] values

    let requiredText fieldName value =
        if String.IsNullOrWhiteSpace value then
            error (String.Concat(fieldName, " is required and must not be empty."))
        else
            Ok value
