namespace Viset

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks

module private FrameHttp =
    let private utf8 = UTF8Encoding false

    let writeResponseAsync
        (stream: NetworkStream)
        status
        contentType
        (body: byte array)
        cancellationToken
        =
        task {
            let header =
                String.Concat(
                    "HTTP/1.1 ",
                    status,
                    "\r\nContent-Type: ",
                    contentType,
                    "\r\nContent-Length: ",
                    body.Length.ToString Globalization.CultureInfo.InvariantCulture,
                    "\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n"
                )

            let headerBytes = utf8.GetBytes header
            do! stream.WriteAsync(headerBytes, cancellationToken)
            do! stream.WriteAsync(body, cancellationToken)
            do! stream.FlushAsync cancellationToken
        }

    let readRequestPathAsync (stream: NetworkStream) (cancellationToken: CancellationToken) =
        task {
            use reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true)
            let! requestLine = reader.ReadLineAsync cancellationToken

            match Option.ofObj requestLine with
            | None -> return None
            | Some line when String.IsNullOrWhiteSpace line -> return None
            | Some line ->
                let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                let mutable headersComplete = false

                while not headersComplete do
                    let! headerLine = reader.ReadLineAsync cancellationToken

                    headersComplete <-
                        match Option.ofObj headerLine with
                        | None -> true
                        | Some value -> String.IsNullOrEmpty value

                if
                    parts.Length <> 3
                    || not (String.Equals(parts[0], "GET", StringComparison.Ordinal))
                then
                    return None
                else
                    let path = parts[1].Split('?', 2)[0]
                    return Some path
        }

type FrameServer
    private (listener: TcpListener, token: string, html: byte array, script: byte array) =
    let cancellation = new CancellationTokenSource()
    let imageLock = obj ()
    let mutable image: CompressedFrame option = None
    let mutable disposed = 0

    let currentImage () =
        lock imageLock (fun () ->
            image
            |> Option.map (fun frame ->
                { frame with
                    Bytes = Array.copy frame.Bytes }))

    let handleClientAsync (client: TcpClient) =
        task {
            use client = client
            use stream = client.GetStream()
            let! path = FrameHttp.readRequestPathAsync stream cancellation.Token
            let rootPath = String.Concat("/", token, "/")

            match path with
            | Some value when String.Equals(value, rootPath, StringComparison.Ordinal) ->
                do!
                    FrameHttp.writeResponseAsync
                        stream
                        "200 OK"
                        "text/html; charset=utf-8"
                        html
                        cancellation.Token
            | Some value when
                String.Equals(
                    value,
                    String.Concat(rootPath, "viset-frame.js"),
                    StringComparison.Ordinal
                )
                ->
                do!
                    FrameHttp.writeResponseAsync
                        stream
                        "200 OK"
                        "text/javascript; charset=utf-8"
                        script
                        cancellation.Token
            | Some value when
                String.Equals(value, String.Concat(rootPath, "image"), StringComparison.Ordinal)
                ->
                match currentImage () with
                | None ->
                    do!
                        FrameHttp.writeResponseAsync
                            stream
                            "404 Not Found"
                            "text/plain; charset=utf-8"
                            (Encoding.UTF8.GetBytes "Frame image is not available.")
                            cancellation.Token
                | Some frame ->
                    let contentType =
                        match frame.Format with
                        | PngImage -> "image/png"
                        | JpegImage -> "image/jpeg"

                    do!
                        FrameHttp.writeResponseAsync
                            stream
                            "200 OK"
                            contentType
                            frame.Bytes
                            cancellation.Token
            | _ ->
                do!
                    FrameHttp.writeResponseAsync
                        stream
                        "404 Not Found"
                        "text/plain; charset=utf-8"
                        (Encoding.UTF8.GetBytes "Not found.")
                        cancellation.Token
        }

    let rec serveAsync () =
        task {
            try
                let! client = listener.AcceptTcpClientAsync cancellation.Token

                try
                    do! handleClientAsync client
                with
                | :? IOException
                | :? SocketException -> ()

                return! serveAsync ()
            with
            | :? OperationCanceledException when cancellation.IsCancellationRequested -> ()
            | :? ObjectDisposedException when cancellation.IsCancellationRequested -> ()
            | :? SocketException when cancellation.IsCancellationRequested -> ()
        }

    let serverTask = serveAsync ()

    member _.Url =
        let endpoint = listener.LocalEndpoint :?> IPEndPoint
        Uri(String.Concat("http://127.0.0.1:", endpoint.Port, "/", token, "/"))

    member _.UpdateImage(frame: CompressedFrame) =
        Media.validateImage frame |> ignore

        lock imageLock (fun () ->
            image <-
                Some
                    { frame with
                        Bytes = Array.copy frame.Bytes })

    member private _.DisposeCoreAsync() =
        task {
            if Interlocked.Exchange(&disposed, 1) = 0 then
                cancellation.Cancel()
                listener.Stop()

                try
                    do! serverTask
                finally
                    cancellation.Dispose()
        }

    interface IAsyncDisposable with
        member this.DisposeAsync() = ValueTask(this.DisposeCoreAsync())

    static member Start(frameSource: FrameSource, device: Device, initialImage: CompressedFrame) =
        Media.validateImage initialImage |> ignore

        let source =
            match frameSource with
            | CustomFrame path ->
                if not (File.Exists path) then
                    invalidArg
                        (nameof frameSource)
                        (String.Concat("Frame HTML does not exist: ", path))

                File.ReadAllText path
            | BuiltInFrame style -> BuiltInFrames.html style device

        let token =
            RandomNumberGenerator.GetBytes 32
            |> Convert.ToHexString
            |> fun value -> value.ToLowerInvariant()

        let html = FrameBootstrap.inject token source |> Encoding.UTF8.GetBytes
        let script = FrameBootstrap.script token device
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        let server = FrameServer(listener, token, html, script)
        server.UpdateImage initialImage
        server
