namespace Viset

open System
open System.Buffers
open System.Collections.Concurrent
open System.Diagnostics
open System.Globalization
open System.IO
open System.Net.WebSockets
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Viset.Serialization

type CdpProtocolError(code: int, message: string, data: JsonElement option) =
    member _.Code = code
    member _.Message = message
    member _.Data = data

    override _.ToString() =
        String.Concat(code.ToString(CultureInfo.InvariantCulture), ": ", message)

[<DebuggerDisplay("CdpEvaluationValue")>]
type CdpEvaluationValue =
    | Undefined
    | Null
    | Boolean of bool
    | Number of double
    | String of string
    | Json of JsonElement

    override value.ToString() =
        match value with
        | Undefined -> "undefined"
        | Null -> "null"
        | Boolean flag -> if flag then "true" else "false"
        | Number number -> number.ToString("R", CultureInfo.InvariantCulture)
        | String text -> text
        | Json json -> json.GetRawText()

[<DebuggerDisplay("CdpEvaluationError")>]
type CdpEvaluationError =
    | Protocol of CdpProtocolError
    | JavaScript of string

    override error.ToString() =
        match error with
        | Protocol protocol -> protocol.ToString()
        | JavaScript message -> message

type CdpProtocolException(error: CdpProtocolError) =
    inherit Exception(String.Concat("CDP protocol error ", error.ToString()))
    member _.Error = error

type CdpTimeoutException(methodName: string, timeout: TimeSpan) =
    inherit
        TimeoutException(
            String.Concat(
                "CDP method '",
                methodName,
                "' exceeded timeout ",
                timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                " ms."
            )
        )

type CdpConnectionException(message: string, innerException: Exception) =
    inherit Exception(message, innerException)

type CdpScreencastFrame =
    { Bytes: byte array
      Timestamp: TimeSpan
      SessionId: int }

    override frame.ToString() =
        frame.Timestamp.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture)
