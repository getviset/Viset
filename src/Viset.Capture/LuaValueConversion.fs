namespace Viset

open System
open System.Text.Json
open Lua

module internal LuaValueConversion =
    open LuaTableHelpers

    let rec tomlValue value =
        match value with
        | TomlValue.String text -> LuaValue text
        | Integer number -> LuaValue(double number)
        | Float number -> LuaValue number
        | TomlValue.Boolean flag -> LuaValue flag
        | TomlValue.DateTime text -> LuaValue text
        | TomlValue.Array values ->
            let table = LuaTable(values.Length, 0)

            values
            |> List.iteri (fun index item -> table[LuaValue(double (index + 1))] <- tomlValue item)

            LuaValue table
        | Table values ->
            let table = LuaTable(0, values.Length)

            values |> List.iter (fun (key, item) -> setValue table key (tomlValue item))

            LuaValue table

    let rec jsonValue (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> LuaValue.Nil
        | JsonValueKind.True -> LuaValue true
        | JsonValueKind.False -> LuaValue false
        | JsonValueKind.String -> LuaValue(element.GetString() |> Option.ofObj |> Option.defaultValue String.Empty)
        | JsonValueKind.Number -> LuaValue(element.GetDouble())
        | JsonValueKind.Array ->
            let values = element.EnumerateArray() |> Seq.toArray

            let table = LuaTable(values.Length, 0)

            values
            |> Array.iteri (fun index value -> table[LuaValue(double (index + 1))] <- jsonValue value)

            LuaValue table
        | JsonValueKind.Object ->
            let properties = element.EnumerateObject() |> Seq.toArray

            let table = LuaTable(0, properties.Length)

            properties
            |> Array.iter (fun property -> setValue table property.Name (jsonValue property.Value))

            LuaValue table
        | kind -> invalidOp (String.Concat("Unsupported JSON value kind: ", kind.ToString()))

    let evaluationValue value =
        match value with
        | Undefined
        | Null -> LuaValue.Nil
        | CdpEvaluationValue.Boolean flag -> LuaValue flag
        | Number number -> LuaValue number
        | CdpEvaluationValue.String text -> LuaValue text
        | Json json -> jsonValue json

    let private dimensionsTable dimensions =
        let table = LuaTable()
        setValue table "width" (LuaValue(double dimensions.Width))
        setValue table "height" (LuaValue(double dimensions.Height))
        table

    let private deviceTable device =
        let table = LuaTable()
        setValue table "name" (LuaValue device.Name)
        setValue table "mobile" (LuaValue device.Mobile)
        setValue table "touch" (LuaValue device.Touch)
        setValue table "device_scale" (LuaValue device.DeviceScale)

        setValue table "viewport" (LuaValue(dimensionsTable device.Viewport))

        match device.Frame with
        | Some frame -> setValue table "frame" (LuaValue(dimensionsTable frame))
        | None -> setValue table "frame" LuaValue.Nil

        table

    let caseContext (plan: CapturePlan) (capture: PlannedCapture) =
        let table = LuaTable()
        setValue table "script_path" (LuaValue plan.ScriptPath)
        setValue table "output" (LuaValue capture.OutputPath)
        setValue table "device" (LuaValue(deviceTable capture.Device))

        let axes = LuaTable(0, capture.Axes.Length)

        capture.Axes
        |> List.iter (fun (key, value) -> setValue axes key (tomlValue value))

        setValue table "axes" (LuaValue axes)

        let data = LuaTable(0, capture.Data.Length)

        capture.Data
        |> List.iter (fun (key, value) -> setValue data key (tomlValue value))

        setValue table "data" (LuaValue data)
        table

    let collectAnimationDurations (destination: ResizeArray<TimeSpan>) value =
        match value with
        | Json json when json.ValueKind = JsonValueKind.Object ->
            let mutable durations = Unchecked.defaultof<JsonElement>

            if
                json.TryGetProperty("update_durations_ms", &durations)
                && durations.ValueKind = JsonValueKind.Array
            then
                for duration in durations.EnumerateArray() do
                    let milliseconds = duration.GetDouble()

                    if Double.IsFinite milliseconds && milliseconds >= 0.0 then
                        destination.Add(TimeSpan.FromMilliseconds milliseconds)
        | _ -> ()
