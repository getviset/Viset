namespace Viset.Tests

open System
open FsUnit.Xunit
open Xunit
open Viset

module RecordingTimelineTests =
    [<Fact>]
    let ``capturing after missed slots should duplicate the previous frame`` () =
        let timeline =
            RecordingTimeline.empty
            |> RecordingTimeline.appendFrame 0
            |> RecordingTimeline.capture 0 1 1
            |> RecordingTimeline.capture 0 3 2

        RecordingTimeline.toArray timeline |> should equal [| 0; 1; 1; 2 |]
        RecordingTimeline.missedSlots timeline |> should equal 1
        RecordingTimeline.duplicatedFrames timeline |> should equal 1

    [<Fact>]
    let ``closing a segment should trim excess frames and accumulate active duration`` () =
        let timeline =
            RecordingTimeline.empty
            |> RecordingTimeline.appendFrame 0
            |> RecordingTimeline.capture 0 1 1
            |> RecordingTimeline.capture 0 3 2

        let closed =
            RecordingTimeline.closeSegment
                (TimeSpan.FromMilliseconds 100.0)
                0
                (TimeSpan.FromMilliseconds 300.0)
                timeline

        RecordingTimeline.toArray closed |> should equal [| 0; 1; 1 |]

        RecordingTimeline.activeDuration closed
        |> should equal (TimeSpan.FromMilliseconds 300.0)
