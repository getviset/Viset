namespace Viset

open System
open System.IO
open System.Text
open System.Text.Json
open Lua

module internal LuaJavascriptArguments =
    let expression (arguments: LuaTable) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)

        let rec writeValue depth (value: LuaValue) =
            if depth > 64 then
                invalidArg (nameof arguments) "JavaScript arguments must not exceed 64 nested tables."

            match value.Type with
            | LuaValueType.Nil -> writer.WriteNullValue()
            | LuaValueType.Boolean -> writer.WriteBooleanValue(value.Read<bool>())
            | LuaValueType.String -> writer.WriteStringValue(value.Read<string>())
            | LuaValueType.Number ->
                let number = value.Read<double>()

                if not (Double.IsFinite number) then
                    invalidArg (nameof arguments) "JavaScript arguments must not contain non-finite numbers."

                writer.WriteNumberValue number
            | LuaValueType.Table -> writeTable (depth + 1) (value.Read<LuaTable>())
            | unsupported ->
                invalidArg
                    (nameof arguments)
                    (String.Concat(
                        "JavaScript arguments cannot contain Lua ",
                        unsupported.ToString().ToLowerInvariant(),
                        " values."
                    ))

        and writeTable depth (table: LuaTable) =
            if table.ArrayLength > 0 && table.HashMapCount > 0 then
                invalidArg (nameof arguments) "JavaScript argument tables must not mix array and object entries."

            elif table.ArrayLength > 0 then
                writer.WriteStartArray()

                for index in 1 .. table.ArrayLength do
                    writeValue depth table[LuaValue(double index)]

                writer.WriteEndArray()

            else
                writer.WriteStartObject()

                for item in table do
                    if item.Key.Type <> LuaValueType.String then
                        invalidArg (nameof arguments) "JavaScript argument object keys must be strings."

                    writer.WritePropertyName(item.Key.Read<string>())

                    writeValue depth item.Value

                writer.WriteEndObject()

        writeTable 0 arguments
        writer.Flush()

        let json = Encoding.UTF8.GetString(stream.ToArray())

        use literalStream = new MemoryStream()
        use literalWriter = new Utf8JsonWriter(literalStream)

        literalWriter.WriteStringValue json
        literalWriter.Flush()

        $"JSON.parse({Encoding.UTF8.GetString(literalStream.ToArray())})"
