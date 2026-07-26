namespace Viset

open System
open System.Diagnostics
open Lua

module internal LuaProcessBindings =
    open LuaTableHelpers

    let create (registry: LuaProcessRegistry) =
        let start =
            hostFunction "viset.process.start" (fun context _ ->
                task {
                    let options = context.GetArgument<LuaTable> 0

                    let startInfo = ProcessStartInfo(requiredString options "file")

                    startInfo.UseShellExecute <- false
                    startInfo.CreateNoWindow <- true
                    startInfo.RedirectStandardOutput <- true
                    startInfo.RedirectStandardError <- true

                    optionalString options "working_directory"
                    |> Option.iter (fun directory -> startInfo.WorkingDirectory <- directory)

                    match getValue options "arguments" |> tryRead<LuaTable> with
                    | Some arguments ->
                        for index in 1 .. arguments.ArrayLength do
                            startInfo.ArgumentList.Add(
                                arguments[LuaValue(double index)].Read<string>()
                            )
                    | None -> ()

                    match getValue options "environment" |> tryRead<LuaTable> with
                    | Some environment ->
                        for item in environment do
                            startInfo.Environment[item.Key.Read<string>()] <-
                                item.Value.Read<string>()
                    | None -> ()

                    let handle = registry.Start startInfo

                    return context.Return(LuaValue(double handle))
                })

        let wait =
            hostFunction "viset.process.wait" (fun context cancellationToken ->
                task {
                    let handle = context.GetArgument<double> 0 |> numberToInt "handle"

                    let timeout =
                        if context.HasArgument 1 then
                            context.GetArgument 1
                            |> durationMilliseconds
                            |> TimeSpan.FromMilliseconds
                        else
                            TimeSpan.FromSeconds 30.0

                    let! result = registry.WaitAsync(handle, timeout, cancellationToken)

                    return context.Return result
                })

        let stop =
            hostFunction "viset.process.stop" (fun context cancellationToken ->
                task {
                    let handle = context.GetArgument<double> 0 |> numberToInt "handle"

                    let! result = registry.StopAsync(handle, cancellationToken)

                    return context.Return result
                })

        let table = LuaTable()
        setValue table "start" (LuaValue start)
        setValue table "wait" (LuaValue wait)
        setValue table "stop" (LuaValue stop)
        table
