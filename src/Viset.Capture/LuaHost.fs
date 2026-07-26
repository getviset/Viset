namespace Viset

open System
open System.Net.Http
open System.Threading
open Lua
open Lua.Standard

module LuaHost =
    let private captureAsync
        (plan: CapturePlan)
        (planned: PlannedCapture)
        (browserOptions: BrowserSessionOptions)
        (cancellationToken: CancellationToken)
        =
        task {
            use! session =
                CaptureSession.LaunchAsync(
                    browserOptions,
                    planned.Device,
                    plan.FrameSource,
                    cancellationToken
                )

            use state = LuaState.Create()
            state.OpenStandardLibraries()
            state.ModuleLoader <- CaptureModuleLoader plan.ScriptDirectory

            use httpClient = new HttpClient(Timeout = Timeout.InfiniteTimeSpan)

            let processes = LuaProcessRegistry()

            let activeCase =
                { Planned = planned
                  Session = session
                  AnimationUpdateDurations = ResizeArray<TimeSpan>()
                  Snapshot = None
                  Recorder = None }

            let visetTable =
                LuaEnvironment.create plan planned activeCase processes httpClient cancellationToken

            state.Environment[LuaValue "viset"] <- LuaValue visetTable

            let mutable primaryError: exn option = None
            let cleanupFailures = ResizeArray<string>()
            let mutable captured: CapturedFile option = None

            let mutable performance: CapturePerformanceMetrics option = None

            let mutable webPPerformance: WebPProductionMetrics option = None

            try
                try
                    let! _ =
                        state
                            .DoStringAsync(
                                LuaBootstrap.source,
                                cancellationToken = cancellationToken
                            )
                            .AsTask()

                    let! _ = state.DoFileAsync(plan.ScriptPath, cancellationToken).AsTask()

                    match planned.Format with
                    | Png ->
                        let bytes =
                            activeCase.Snapshot
                            |> Option.defaultWith (fun () ->
                                invalidOp "A .png capture must call viset.snapshot exactly once.")

                        captured <-
                            Some
                                { Capture = planned
                                  Bytes = bytes
                                  FrameTicksMs = [] }

                    | WebP ->
                        let recorder =
                            activeCase.Recorder
                            |> Option.defaultWith (fun () ->
                                invalidOp "A .webp capture must call viset.record exactly once.")

                        let! animation = recorder.FinalizeAsync cancellationToken

                        performance <- Some animation.Metrics

                        webPPerformance <- Some animation.Encoded.Metrics

                        captured <-
                            Some
                                { Capture = planned
                                  Bytes = animation.Encoded.Bytes
                                  FrameTicksMs = animation.Encoded.FrameTicksMs }
                with error ->
                    primaryError <- Some error

                match activeCase.Recorder with
                | Some recorder when recorder.IsActive ->
                    try
                        do! recorder.StopAsync()
                    with error ->
                        cleanupFailures.Add(
                            String.Concat("Recording cleanup failed: ", error.Message)
                        )
                | _ -> ()

                let! processFailures = processes.CleanupAsync()

                processFailures |> List.iter cleanupFailures.Add

                activeCase.Recorder
                |> Option.iter (fun recorder ->
                    try
                        (recorder :> IDisposable).Dispose()
                    with error ->
                        cleanupFailures.Add(
                            String.Concat("Recording spool cleanup failed: ", error.Message)
                        ))

                try
                    do! (session :> IAsyncDisposable).DisposeAsync().AsTask()
                with error ->
                    cleanupFailures.Add error.Message
            with error ->
                primaryError <- primaryError |> Option.orElse (Some error)

            match primaryError, List.ofSeq cleanupFailures with
            | Some error, [] -> raise error
            | None, [] -> ()
            | None, failures -> raise (InvalidOperationException(String.Join(" ", failures)))
            | Some error, failures ->
                raise (
                    InvalidOperationException(
                        String.Concat(
                            error.Message,
                            " Cleanup also failed: ",
                            String.Join(" ", failures)
                        ),
                        error
                    )
                )

            let completed =
                captured
                |> Option.defaultWith (fun () ->
                    invalidOp "Capture completed without output bytes.")

            let writtenPath = Output.write plan.Force completed

            return
                { Path = writtenPath
                  Format = planned.Format
                  FrameTicksMs = completed.FrameTicksMs
                  Performance = performance
                  WebPPerformance = webPPerformance
                  AnimationUpdateDurations = List.ofSeq activeCase.AnimationUpdateDurations }
        }

    let runAsync
        (_toolVersion: string)
        (plan: CapturePlan)
        (browser: BrowserExecutable)
        (cancellationToken: CancellationToken)
        =
        task {
            Output.preflight plan

            let browserOptions =
                BrowserSessionOptions(browser.ExecutablePath, plan.BrowserArguments)

            let outputs = ResizeArray<CaptureOutputResult>()

            for planned in plan.Captures do
                let! output = captureAsync plan planned browserOptions cancellationToken

                outputs.Add output

            return { Outputs = List.ofSeq outputs }
        }
