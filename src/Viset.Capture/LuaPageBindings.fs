namespace Viset

open System
open System.Threading
open System.Threading.Tasks
open Lua

module internal LuaPageBindings =
    open LuaTableHelpers

    type Tables = { Page: LuaTable; Emulation: LuaTable }

    let create (activeCase: ActiveCase) =
        let navigate =
            hostFunction "viset.page.navigate" (fun context cancellationToken ->
                task {
                    let uri = Uri(context.GetArgument<string> 0, UriKind.Absolute)

                    do! activeCase.Session.Page.NavigateAsync(uri, cancellationToken)

                    return context.Return()
                })

        let evaluate =
            hostFunction "viset.page.evaluate" (fun context cancellationToken ->
                task {
                    let script = context.GetArgument<string> 0

                    let expression =
                        if context.HasArgument 1 then
                            LuaJavascript.evaluateExpression script (context.GetArgument<LuaTable> 1)
                        else
                            script

                    let! result = activeCase.Session.Page.EvaluateAsync(expression, cancellationToken)

                    match result with
                    | Ok value -> return context.Return(LuaValueConversion.evaluationValue value)
                    | Error error -> return raise (InvalidOperationException(error.ToString()))
                })

        let waitFor =
            hostFunction "viset.page.wait_for" (fun context cancellationToken ->
                task {
                    let script = context.GetArgument<string> 0

                    let timeoutMilliseconds = context.GetArgument 1 |> durationMilliseconds

                    use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellationToken

                    timeout.CancelAfter(TimeSpan.FromMilliseconds timeoutMilliseconds)

                    let mutable ready = false

                    while not ready do
                        let! result = activeCase.Session.Page.EvaluateAsync(script, timeout.Token)

                        match result with
                        | Ok(CdpEvaluationValue.Boolean value) -> ready <- value
                        | Ok _ -> ready <- false
                        | Error error -> raise (InvalidOperationException(error.ToString()))

                        if not ready then
                            do! Task.Delay(20, timeout.Token)

                    return context.Return()
                })

        let animate =
            hostFunction "viset.page.animate" (fun context cancellationToken ->
                task {
                    let options = context.GetArgument<LuaTable> 0

                    let duration = getValue options "duration" |> durationMilliseconds

                    let update = requiredString options "update"

                    let easing = optionalString options "easing" |> Option.defaultValue "linear"

                    let script = LuaJavascript.animationExpression duration update easing

                    let! result = activeCase.Session.Page.EvaluateAsync(script, cancellationToken)

                    match result with
                    | Error error -> return raise (InvalidOperationException(error.ToString()))
                    | Ok value ->
                        LuaValueConversion.collectAnimationDurations activeCase.AnimationUpdateDurations value

                        return context.Return()
                })

        let applyEmulation =
            hostFunction "viset.emulation.apply" (fun context cancellationToken ->
                task {
                    let device = context.GetArgument<LuaTable> 0

                    let viewport = getValue device "viewport" |> fun value -> value.Read<LuaTable>()

                    let width =
                        getValue viewport "width"
                        |> fun value -> value.Read<double>()
                        |> numberToInt "width"

                    let height =
                        getValue viewport "height"
                        |> fun value -> value.Read<double>()
                        |> numberToInt "height"

                    let scale = optionalNumber device "device_scale" 1.0

                    let mobile =
                        match getValue device "mobile" with
                        | value when value.Type = LuaValueType.Nil -> false
                        | value -> value.Read<bool>()

                    let touch =
                        match getValue device "touch" with
                        | value when value.Type = LuaValueType.Nil -> false
                        | value -> value.Read<bool>()

                    do!
                        activeCase.Session.Page.ConfigureEmulationAsync(
                            width,
                            height,
                            scale,
                            mobile,
                            touch,
                            cancellationToken
                        )

                    return context.Return()
                })

        let touch =
            hostFunction "viset.emulation.touch" (fun context cancellationToken ->
                task {
                    let x = context.GetArgument<double> 0

                    let y = context.GetArgument<double> 1

                    do! activeCase.Session.Page.TouchAsync(x, y, cancellationToken)

                    return context.Return()
                })

        let pageTable = LuaTable()
        setValue pageTable "navigate" (LuaValue navigate)
        setValue pageTable "evaluate" (LuaValue evaluate)
        setValue pageTable "wait_for" (LuaValue waitFor)
        setValue pageTable "animate" (LuaValue animate)

        let emulationTable = LuaTable()
        setValue emulationTable "apply" (LuaValue applyEmulation)
        setValue emulationTable "touch" (LuaValue touch)

        { Page = pageTable
          Emulation = emulationTable }
