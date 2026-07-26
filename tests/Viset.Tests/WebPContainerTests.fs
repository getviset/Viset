namespace Viset.Tests

open System
open FsUnit.Xunit
open Xunit
open Viset
open Viset.Tests.TestSupport

module WebPContainerTests =
    [<Fact>]
    let ``parsing an animated container should count frames without mutating bytes`` () =
        let webP = animatedWebP ()
        let original = Array.copy webP
        let container = WebPContainerParser.parse webP

        WebPContainerInspection.frameCount container |> should equal 2
        webP |> should equal original

    [<Fact>]
    let ``planning a duration patch should correct only the final frame`` () =
        let webP = animatedWebP ()
        let container = WebPContainerParser.parse webP

        let patch =
            WebPDurationNormalization.plan
                WebPEncoding.MaximumFrameDurationMilliseconds
                40
                container
            |> Option.defaultWith (fun () -> failwith "Expected a duration patch.")

        patch.Duration |> should equal 30
        int webP[patch.Offset] |> should equal 20

    [<Fact>]
    let ``an invalid RIFF size should return an explicit diagnostic`` () =
        let webP = animatedWebP ()
        writeUInt32LittleEndian webP 4 0u

        (fun () -> WebPContainerParser.parse webP |> ignore)
        |> shouldFailWithMessage "An encoder returned a WebP container with an invalid RIFF size."

    [<Fact>]
    let ``a truncated WebP chunk should return an explicit diagnostic`` () =
        let webP = animatedWebP ()
        writeUInt32LittleEndian webP 16 UInt32.MaxValue

        (fun () -> WebPContainerParser.parse webP |> ignore)
        |> shouldFailWithMessage "An encoder returned a truncated WebP chunk."

    [<Fact>]
    let ``a WebP container without an image should return an explicit diagnostic`` () =
        let webP = webPWithoutImageFrame ()

        (fun () ->
            webP
            |> WebPContainerParser.parse
            |> WebPContainerInspection.frameCount
            |> ignore)
        |> shouldFailWithMessage "An encoder returned a WebP container without an image frame."
