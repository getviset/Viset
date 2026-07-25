namespace Viset

open System
open System.Globalization
open System.Threading
open System.Threading.Tasks
open Lua

module internal LuaTableHelpers =
    let setValue (table: LuaTable) (key: string) (value: LuaValue) = table[LuaValue key] <- value

    let getValue (table: LuaTable) (key: string) = table[LuaValue key]

    let tryRead<'T> (value: LuaValue) =
        let mutable result = Unchecked.defaultof<'T>

        if value.TryRead<'T>(&result) then Some result else None

    let requiredString (table: LuaTable) key =
        match getValue table key |> tryRead<string> with
        | Some value when not (String.IsNullOrWhiteSpace value) -> value
        | _ -> invalidArg key (String.Concat(key, " is required and must be a non-empty string."))

    let optionalString (table: LuaTable) key =
        match getValue table key with
        | value when value.Type = LuaValueType.Nil -> None
        | value ->
            match tryRead<string> value with
            | Some text when not (String.IsNullOrWhiteSpace text) -> Some text
            | _ -> invalidArg key (String.Concat(key, " must be a non-empty string."))

    let optionalNumber (table: LuaTable) key defaultValue =
        match getValue table key with
        | value when value.Type = LuaValueType.Nil -> defaultValue
        | value ->
            match tryRead<double> value with
            | Some number when Double.IsFinite number -> number
            | _ -> invalidArg key (String.Concat(key, " must be a finite number."))

    let numberToInt label value =
        if
            not (Double.IsFinite value)
            || value < double Int32.MinValue
            || value > double Int32.MaxValue
            || Math.Truncate value <> value
        then
            invalidArg label (String.Concat(label, " must be an integer."))

        int value

    let tableValue values =
        let table = LuaTable()

        values |> List.iter (fun (key, value) -> setValue table key value)

        LuaValue table

    let hostFunction
        (name: string)
        (operation: LuaFunctionExecutionContext -> CancellationToken -> Task<int>)
        : LuaFunction =
        LuaFunction(
            name,
            Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>>(fun context cancellationToken ->
                ValueTask<int>(
                    task {
                        try
                            return! operation context cancellationToken
                        with
                        | :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                            return raise (OperationCanceledException cancellationToken)
                        | :? OperationCanceledException as error ->
                            return raise (TimeoutException(String.Concat(name, " timed out."), error))
                        | error ->
                            return raise (InvalidOperationException(String.Concat(name, ": ", error.Message), error))
                    }
                ))
        )

    let durationMilliseconds (value: LuaValue) =
        let validate milliseconds =
            if not (Double.IsFinite milliseconds) || milliseconds <= 0.0 then
                invalidArg "duration" "duration must be a positive finite value."

            milliseconds

        match tryRead<double> value with
        | Some number -> validate number
        | None ->
            match tryRead<string> value with
            | None -> invalidArg "duration" "duration must be a number of milliseconds or a string ending in ms or s."
            | Some text ->
                let trimmed = text.Trim()

                let parse (suffix: string) multiplier =
                    let numberText = trimmed.Substring(0, trimmed.Length - suffix.Length)

                    match Double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture) with
                    | true, number -> validate (number * multiplier)
                    | _ -> invalidArg "duration" (String.Concat("Invalid duration: ", text))

                if trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase) then
                    parse "ms" 1.0
                elif trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase) then
                    parse "s" 1000.0
                else
                    invalidArg "duration" "duration strings must end in ms or s."
