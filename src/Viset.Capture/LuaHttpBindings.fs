namespace Viset

open System
open System.Diagnostics
open System.Net.Http
open System.Threading
open Lua

module internal LuaHttpBindings =
    open LuaTableHelpers
    open System.Threading.Tasks

    let private responseTable (response: HttpResponseMessage) (body: string) =
        let headers = LuaTable()

        for header in response.Headers do
            setValue headers header.Key (LuaValue(String.Join(",", header.Value)))

        for header in response.Content.Headers do
            setValue headers header.Key (LuaValue(String.Join(",", header.Value)))

        tableValue
            [ "status", LuaValue(double (int response.StatusCode))
              "headers", LuaValue headers
              "body", LuaValue body ]

    let private sendGetAsync
        (client: HttpClient)
        (options: LuaTable)
        (timeoutMilliseconds: float)
        (cancellationToken: CancellationToken)
        =
        task {
            let uri = Uri(requiredString options "url", UriKind.Absolute)

            use request = new HttpRequestMessage(HttpMethod.Get, uri)

            match getValue options "headers" |> tryRead<LuaTable> with
            | Some headers ->
                for item in headers do
                    request.Headers.TryAddWithoutValidation(item.Key.Read<string>(), item.Value.Read<string>())
                    |> ignore
            | None -> ()

            use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeout.CancelAfter(TimeSpan.FromMilliseconds timeoutMilliseconds: TimeSpan)

            use! response = client.SendAsync(request, timeout.Token)

            let! body = response.Content.ReadAsStringAsync timeout.Token

            return response, body
        }

    let create (client: HttpClient) =
        let get =
            hostFunction "viset.http.get" (fun context cancellationToken ->
                task {
                    let options = context.GetArgument<LuaTable> 0

                    let timeoutMilliseconds =
                        match getValue options "timeout" with
                        | value when value.Type = LuaValueType.Nil -> 30000.0
                        | value -> durationMilliseconds value

                    let! response, body = sendGetAsync client options timeoutMilliseconds cancellationToken

                    use response = response
                    return context.Return(responseTable response body)
                })

        let wait =
            hostFunction "viset.http.wait" (fun context cancellationToken ->
                task {
                    let options = context.GetArgument<LuaTable> 0

                    let timeoutMilliseconds =
                        match getValue options "timeout" with
                        | value when value.Type = LuaValueType.Nil -> 30000.0
                        | value -> durationMilliseconds value

                    let stopwatch = Stopwatch.StartNew()
                    let mutable completed = None

                    while completed.IsNone && stopwatch.Elapsed.TotalMilliseconds < timeoutMilliseconds do
                        let remaining = timeoutMilliseconds - stopwatch.Elapsed.TotalMilliseconds

                        let requestTimeout = max 1.0 (min 500.0 remaining)

                        try
                            let! response, body = sendGetAsync client options requestTimeout cancellationToken

                            use response = response

                            if int response.StatusCode >= 200 && int response.StatusCode <= 299 then
                                completed <- Some(responseTable response body)
                        with
                        | :? HttpRequestException -> ()
                        | :? OperationCanceledException when not cancellationToken.IsCancellationRequested -> ()

                        if completed.IsNone then
                            do! Task.Delay(50, cancellationToken)

                    match completed with
                    | Some value -> return context.Return value
                    | None ->
                        return
                            raise (TimeoutException "The HTTP endpoint did not return a 2xx response before timeout.")
                })

        let table = LuaTable()
        setValue table "get" (LuaValue get)
        setValue table "wait" (LuaValue wait)
        table
