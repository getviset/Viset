namespace Viset

open System
open System.Buffers
open System.Collections.Concurrent
open System.IO
open System.Net.WebSockets
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Viset.Serialization

type internal CdpTransport private (socket: ClientWebSocket, commandTimeout: TimeSpan) =
    let pending =
        ConcurrentDictionary<int64, TaskCompletionSource<CdpIncomingMessageModel>>()

    let pageLoadEvents = Channel.CreateUnbounded<CdpIncomingMessageModel>()
    let screencastEvents = Channel.CreateUnbounded<CdpIncomingMessageModel>()
    let sendLock = new SemaphoreSlim(1, 1)
    let readerCancellation = new CancellationTokenSource()
    let mutable nextId = 0L
    let mutable disposed = 0

    let protocolError (model: CdpErrorModel) =
        let data =
            if model.Data.ValueKind = JsonValueKind.Undefined then
                None
            else
                Some(model.Data.Clone())

        CdpProtocolError(model.Code, model.Message, data)

    let failPending (error: Exception) =
        for entry in pending do
            match pending.TryRemove entry.Key with
            | true, completion -> completion.TrySetException error |> ignore
            | false, _ -> ()

        pageLoadEvents.Writer.TryComplete error |> ignore
        screencastEvents.Writer.TryComplete error |> ignore

    let eventChannel methodName =
        match methodName with
        | "Page.loadEventFired" -> pageLoadEvents
        | "Page.screencastFrame" -> screencastEvents
        | _ -> invalidArg (nameof methodName) (String.Concat("Unsupported CDP event: ", methodName))

    let clearEvents methodName =
        let channel = eventChannel methodName
        let mutable ignored = Unchecked.defaultof<CdpIncomingMessageModel>

        while channel.Reader.TryRead(&ignored) do
            ()

    let readLoop () =
        task {
            let buffer = ArrayPool<byte>.Shared.Rent 8192

            try
                try
                    while not readerCancellation.IsCancellationRequested do
                        use message = new MemoryStream()
                        let mutable complete = false

                        while not complete do
                            let! result = socket.ReceiveAsync(ArraySegment<byte> buffer, readerCancellation.Token)

                            if result.MessageType = WebSocketMessageType.Close then
                                raise (WebSocketException "The CDP WebSocket closed unexpectedly.")

                            message.Write(buffer, 0, result.Count)

                            if message.Length > 16L * 1024L * 1024L then
                                raise (InvalidDataException "A CDP message exceeded 16 MiB.")

                            complete <- result.EndOfMessage

                        let incoming = CdpJson.DeserializeIncoming(message.ToArray())

                        if incoming.Id.HasValue then
                            match pending.TryRemove incoming.Id.Value with
                            | true, completion -> completion.TrySetResult incoming |> ignore
                            | false, _ -> ()
                        else
                            match incoming.Method with
                            | "Page.loadEventFired" -> pageLoadEvents.Writer.TryWrite incoming |> ignore
                            | "Page.screencastFrame" -> screencastEvents.Writer.TryWrite incoming |> ignore
                            | _ -> ()
                with
                | :? OperationCanceledException when readerCancellation.IsCancellationRequested ->
                    failPending (OperationCanceledException "The CDP reader was stopped.")
                | error -> failPending error
            finally
                ArrayPool<byte>.Shared.Return buffer
        }

    let readerTask = readLoop ()

    member _.SendCommandAsync
        (methodName: string, serialize: int64 -> byte array, cancellationToken: CancellationToken)
        =
        task {
            if Volatile.Read(&disposed) <> 0 then
                raise (ObjectDisposedException(nameof CdpTransport))

            let id = Interlocked.Increment(&nextId)

            let completion =
                TaskCompletionSource<CdpIncomingMessageModel> TaskCreationOptions.RunContinuationsAsynchronously

            if not (pending.TryAdd(id, completion)) then
                raise (InvalidOperationException "A duplicate CDP command ID was generated.")

            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeoutCancellation.CancelAfter commandTimeout

            try
                let payload = serialize id
                do! sendLock.WaitAsync timeoutCancellation.Token

                try
                    do!
                        socket.SendAsync(
                            ArraySegment<byte> payload,
                            WebSocketMessageType.Text,
                            true,
                            timeoutCancellation.Token
                        )
                finally
                    sendLock.Release() |> ignore

                let! incoming = completion.Task.WaitAsync timeoutCancellation.Token
                pending.TryRemove id |> ignore

                match Option.ofObj incoming.Error with
                | None -> return Ok incoming.Result
                | Some incomingError -> return Error(protocolError incomingError)
            with
            | :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                pending.TryRemove id |> ignore
                return raise (CdpTimeoutException(methodName, commandTimeout))
            | error ->
                pending.TryRemove id |> ignore
                return raise error
        }

    member this.SendEmptyAsync(methodName: string, cancellationToken: CancellationToken) =
        this.SendCommandAsync(
            methodName,
            (fun id -> CdpJson.SerializeCommand(id, methodName, CdpEmptyParameters())),
            cancellationToken
        )

    member _.ClearEvents(methodName: string) = clearEvents methodName

    member _.ReadEventAsync(methodName: string, cancellationToken: CancellationToken) =
        task {
            let channel = eventChannel methodName

            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeoutCancellation.CancelAfter commandTimeout

            try
                return! channel.Reader.ReadAsync(timeoutCancellation.Token).AsTask()
            with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                return raise (CdpTimeoutException(methodName, commandTimeout))
        }

    member private _.DisposeCoreAsync() =
        task {
            if Interlocked.Exchange(&disposed, 1) = 0 then
                readerCancellation.Cancel()

                try
                    if
                        socket.State = WebSocketState.Open
                        || socket.State = WebSocketState.CloseReceived
                    then
                        use closeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds 1.0)

                        do!
                            socket.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Viset session closed.",
                                closeCancellation.Token
                            )
                with _ ->
                    ()

                socket.Dispose()

                try
                    do! readerTask
                with _ ->
                    ()

                sendLock.Dispose()
                readerCancellation.Dispose()
        }

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask(this.DisposeCoreAsync())

    static member ConnectAsync(endpoint: Uri, commandTimeout: TimeSpan, cancellationToken: CancellationToken) =
        task {
            ArgumentNullException.ThrowIfNull endpoint

            if commandTimeout <= TimeSpan.Zero then
                invalidArg (nameof commandTimeout) "CDP command timeout must be positive."

            let socket = new ClientWebSocket()

            use connectCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            connectCancellation.CancelAfter commandTimeout

            try
                do! socket.ConnectAsync(endpoint, connectCancellation.Token)
                return CdpTransport(socket, commandTimeout)
            with error ->
                socket.Dispose()

                if
                    error :? OperationCanceledException
                    && not cancellationToken.IsCancellationRequested
                then
                    return
                        raise (
                            CdpConnectionException(
                                String.Concat("Timed out connecting to CDP endpoint ", endpoint.AbsoluteUri, "."),
                                error
                            )
                        )
                else
                    return
                        raise (
                            CdpConnectionException(
                                String.Concat("Failed to connect to CDP endpoint ", endpoint.AbsoluteUri, "."),
                                error
                            )
                        )
        }
