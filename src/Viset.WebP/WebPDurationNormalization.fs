namespace Viset

type internal WebPDurationPatch = { Offset: int; Duration: int }

module internal WebPDurationNormalization =
    let plan maximumDuration expectedDuration container =
        let frames =
            container.Chunks
            |> List.choose (fun chunk ->
                match chunk.Kind, chunk.AnimationDuration, chunk.AnimationDurationOffset with
                | AnimationFrame, Some duration, Some offset -> Some(duration, offset)
                | _ -> None)

        match List.tryLast frames with
        | None -> None
        | Some(currentDuration, durationOffset) ->
            let actualDuration = frames |> List.sumBy fst
            let adjusted = currentDuration + expectedDuration - actualDuration

            if adjusted <= 0 || adjusted > maximumDuration then
                invalidOp
                    "FFmpeg produced a WebP timeline that Viset could not normalize without losing duration."

            Some
                { Offset = durationOffset
                  Duration = adjusted }
