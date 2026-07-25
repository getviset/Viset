namespace Viset

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Lua

type private ManagedProcess(childProcess: Process, standardOutput: Task<string>, standardError: Task<string>) =
    member _.Process = childProcess
    member _.StandardOutput = standardOutput
    member _.StandardError = standardError

type internal LuaProcessRegistry() =
    let processes = Dictionary<int, ManagedProcess>()
    let syncRoot = obj ()
    let mutable nextHandle = 0

    let remove handle =
        lock syncRoot (fun () ->
            match processes.TryGetValue handle with
            | true, childProcess ->
                processes.Remove handle |> ignore
                Some childProcess
            | false, _ -> None)

    let find handle =
        lock syncRoot (fun () ->
            match processes.TryGetValue handle with
            | true, childProcess -> Some childProcess
            | false, _ -> None)

    let resultAsync handle (managed: ManagedProcess) =
        task {
            let! standardOutput = managed.StandardOutput
            let! standardError = managed.StandardError
            let exitCode = managed.Process.ExitCode

            remove handle |> Option.iter (fun value -> value.Process.Dispose())

            return
                LuaTableHelpers.tableValue
                    [ "exit_code", LuaValue(double exitCode)
                      "stdout", LuaValue standardOutput
                      "stderr", LuaValue standardError ]
        }

    member _.Start(startInfo: ProcessStartInfo) =
        let childProcess =
            Process.Start startInfo
            |> Option.ofObj
            |> Option.defaultWith (fun () -> invalidOp "Process could not be started.")

        let managed =
            ManagedProcess(
                childProcess,
                childProcess.StandardOutput.ReadToEndAsync(),
                childProcess.StandardError.ReadToEndAsync()
            )

        lock syncRoot (fun () ->
            nextHandle <- nextHandle + 1
            processes.Add(nextHandle, managed)
            nextHandle)

    member _.WaitAsync(handle: int, timeout: TimeSpan, cancellationToken: CancellationToken) =
        task {
            let managed =
                find handle
                |> Option.defaultWith (fun () -> invalidOp "The process handle is not active.")

            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeoutCancellation.CancelAfter timeout

            do! managed.Process.WaitForExitAsync timeoutCancellation.Token

            return! resultAsync handle managed
        }

    member _.StopAsync(handle: int, cancellationToken: CancellationToken) =
        task {
            let managed =
                find handle
                |> Option.defaultWith (fun () -> invalidOp "The process handle is not active.")

            if not managed.Process.HasExited then
                managed.Process.Kill true

            do! managed.Process.WaitForExitAsync cancellationToken

            return! resultAsync handle managed
        }

    member this.CleanupAsync() =
        task {
            let handles = lock syncRoot (fun () -> processes.Keys |> Seq.toArray)

            let failures = ResizeArray<string>()

            for handle in handles do
                try
                    let! _ = this.StopAsync(handle, CancellationToken.None)

                    ()
                with error ->
                    failures.Add error.Message

            return List.ofSeq failures
        }
