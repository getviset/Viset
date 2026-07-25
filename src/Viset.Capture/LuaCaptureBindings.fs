namespace Viset

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Lua

module internal LuaCaptureBindings =
    open LuaTableHelpers

    type Functions =
        { Snapshot: LuaFunction
          Duration: LuaFunction
          Now: LuaFunction
          Sleep: LuaFunction
          RecordingCreate: LuaFunction
          RecordingStart: LuaFunction
          RecordingStop: LuaFunction
          RecordingActive: LuaFunction }

    let create
        (plan: CapturePlan)
        (planned: PlannedCapture)
        (activeCase: ActiveCase)
        (cancellationToken: CancellationToken)
        =
        let snapshot =
            hostFunction "viset.snapshot" (fun context operationCancellation ->
                task {
                    match planned.Format with
                    | WebP -> invalidOp "viset.snapshot is valid only for .png output."
                    | Png -> ()

                    if activeCase.Snapshot.IsSome then
                        invalidOp "A .png capture must call viset.snapshot exactly once."

                    let! bytes = activeCase.Session.CapturePngAsync operationCancellation

                    activeCase.Snapshot <- Some bytes
                    return context.Return()
                })

        let duration =
            hostFunction "viset.__duration_ms" (fun context _ ->
                task {
                    let milliseconds = context.GetArgument 0 |> durationMilliseconds

                    return context.Return(LuaValue milliseconds)
                })

        let now =
            hostFunction "viset.__now_ms" (fun context _ ->
                task {
                    let milliseconds =
                        double (Stopwatch.GetTimestamp()) * 1000.0 / double Stopwatch.Frequency

                    return context.Return(LuaValue milliseconds)
                })

        let sleep =
            hostFunction "viset.__sleep_ms" (fun context operationCancellation ->
                task {
                    let milliseconds = context.GetArgument<double> 0

                    if not (Double.IsFinite milliseconds) || milliseconds <= 0.0 then
                        invalidArg "duration" "sleep duration must be a positive finite number."

                    do! Task.Delay(TimeSpan.FromMilliseconds milliseconds, operationCancellation)

                    return context.Return()
                })

        let recordingCreate =
            hostFunction "viset.__recording_create" (fun context _ ->
                task {
                    match planned.Format with
                    | Png -> invalidOp "viset.record is valid only for .webp output."
                    | WebP -> ()

                    if activeCase.Recorder.IsSome then
                        invalidOp "A .webp capture may create exactly one recording."

                    activeCase.Recorder <-
                        Some(
                            RecordingController.CreateScreencast(
                                activeCase.Session,
                                plan.FramesPerSecond,
                                plan.WebP,
                                cancellationToken
                            )
                        )

                    return context.Return()
                })

        let recordingStart =
            hostFunction "recording:start" (fun context _ ->
                task {
                    let recorder =
                        activeCase.Recorder
                        |> Option.defaultWith (fun () -> invalidOp "viset.record must be called first.")

                    do! recorder.StartAsync()
                    return context.Return()
                })

        let recordingStop =
            hostFunction "recording:stop" (fun context _ ->
                task {
                    let recorder =
                        activeCase.Recorder
                        |> Option.defaultWith (fun () -> invalidOp "viset.record must be called first.")

                    do! recorder.StopAsync()
                    return context.Return()
                })

        let recordingActive =
            hostFunction "recording:active" (fun context _ ->
                task {
                    let isActive =
                        activeCase.Recorder |> Option.exists (fun recorder -> recorder.IsActive)

                    return context.Return(LuaValue isActive)
                })

        { Snapshot = snapshot
          Duration = duration
          Now = now
          Sleep = sleep
          RecordingCreate = recordingCreate
          RecordingStart = recordingStart
          RecordingStop = recordingStop
          RecordingActive = recordingActive }
