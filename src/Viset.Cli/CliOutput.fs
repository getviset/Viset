namespace Viset

open System
open System.Globalization

module internal CliOutput =
    let writeErrors errors =
        errors
        |> List.iter (fun message -> Console.Error.WriteLine(String.Concat("error: ", message)))


    let writePlan (plan: CapturePlan) =
        Console.Out.WriteLine(String.Concat("output: ", plan.OutputPath))

        Console.Out.WriteLine(String.Concat("captures: ", plan.Captures.Length.ToString(CultureInfo.InvariantCulture)))

        plan.Captures
        |> List.iter (fun capture ->
            Console.Out.WriteLine(String.Concat(capture.Format.ToString(), ": ", capture.OutputRelativePath)))

    let private percentile percentileValue (durations: TimeSpan list) =
        match durations |> List.sort with
        | [] -> TimeSpan.Zero
        | sorted ->
            let index =
                int (Math.Ceiling(percentileValue * double sorted.Length)) - 1
                |> max 0
                |> min (sorted.Length - 1)

            sorted[index]

    let writeWebPPerformance (output: CaptureOutputResult) =
        match output.WebPPerformance with
        | None -> ()
        | Some metrics ->
            let perFrame =
                metrics.TotalDuration.TotalMilliseconds / double (max 1 metrics.FrameCount)

            let decodeP95 = percentile 0.95 metrics.DecodeDurations
            let encodeP95 = percentile 0.95 metrics.EncodeDurations

            let decodeP95Text =
                if List.isEmpty metrics.DecodeDurations then
                    "n/a"
                else
                    decodeP95.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture)

            let encodeP95Text =
                if List.isEmpty metrics.EncodeDurations then
                    "n/a"
                else
                    encodeP95.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture)

            Console.Out.WriteLine(
                String.Concat(
                    "webp_metrics: ",
                    output.Path,
                    " encoder=",
                    metrics.Encoder.ToString(),
                    " pipeline=",
                    metrics.Pipeline.ToString(),
                    " frames=",
                    metrics.FrameCount.ToString(CultureInfo.InvariantCulture),
                    " encoded=",
                    metrics.EncodedFrameCount.ToString(CultureInfo.InvariantCulture),
                    " spilled=",
                    metrics.SpilledFrameCount.ToString(CultureInfo.InvariantCulture),
                    " workers=",
                    metrics.WorkerCount.ToString(CultureInfo.InvariantCulture),
                    " total_ms=",
                    metrics.TotalDuration.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                    " per_frame_ms=",
                    perFrame.ToString("0.00", CultureInfo.InvariantCulture),
                    " decode_p95_ms=",
                    decodeP95Text,
                    " encode_p95_ms=",
                    encodeP95Text,
                    " mux_ms=",
                    metrics.MuxDuration.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture)
                )
            )

            if not (List.isEmpty metrics.EncodeDurations) && encodeP95.TotalMilliseconds > 10.0 then
                Console.Error.WriteLine(
                    String.Concat(
                        "warning: WebP frame encoding exceeded the 10 ms p95 ceiling for ",
                        output.Path,
                        ": p95_ms=",
                        encodeP95.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        ", target_ms=10.00"
                    )
                )

            if perFrame > 5.0 then
                Console.Error.WriteLine(
                    String.Concat(
                        "warning: WebP production throughput exceeded 5 ms per frame for ",
                        output.Path,
                        ": per_frame_ms=",
                        perFrame.ToString("0.00", CultureInfo.InvariantCulture),
                        ", target_ms=5.00"
                    )
                )

    let writePerformance framesPerSecond (output: CaptureOutputResult) =
        match output.Performance with
        | None -> ()
        | Some metrics ->
            let p95 = percentile 0.95 metrics.CaptureDurations
            let p99 = percentile 0.99 metrics.CaptureDurations
            let maximum = metrics.CaptureDurations |> List.fold max TimeSpan.Zero

            Console.Out.WriteLine(
                String.Concat(
                    "metrics: ",
                    output.Path,
                    " source=",
                    metrics.Source.ToString(),
                    " pipeline=",
                    metrics.Pipeline.ToString(),
                    " frames=",
                    metrics.FrameCount.ToString(CultureInfo.InvariantCulture),
                    " unique=",
                    metrics.UniqueFrameCount.ToString(CultureInfo.InvariantCulture),
                    " p95_ms=",
                    p95.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                    " p99_ms=",
                    p99.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                    " max_ms=",
                    maximum.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                    " missed=",
                    metrics.MissedSlots.ToString(CultureInfo.InvariantCulture),
                    " duplicated=",
                    metrics.DuplicatedFrames.ToString(CultureInfo.InvariantCulture),
                    " dropped=",
                    metrics.DroppedFrames.ToString(CultureInfo.InvariantCulture)
                )
            )

            let budget = TimeSpan.FromSeconds(1.0 / double framesPerSecond)

            let acquisitionOverruns =
                metrics.CaptureDurations |> List.filter (fun duration -> duration > budget)

            if not (List.isEmpty acquisitionOverruns) || metrics.MissedSlots > 0 then
                Console.Error.WriteLine(
                    String.Concat(
                        "warning: frame acquisition exceeded the ",
                        budget.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        " ms budget for ",
                        output.Path,
                        ": overruns=",
                        acquisitionOverruns.Length.ToString(CultureInfo.InvariantCulture),
                        ", missed=",
                        metrics.MissedSlots.ToString(CultureInfo.InvariantCulture),
                        ", duplicated=",
                        metrics.DuplicatedFrames.ToString(CultureInfo.InvariantCulture),
                        ", dropped=",
                        metrics.DroppedFrames.ToString(CultureInfo.InvariantCulture),
                        ", p95_ms=",
                        p95.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        ", p99_ms=",
                        p99.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        ", max_ms=",
                        maximum.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture)
                    )
                )

            let animationOverruns =
                output.AnimationUpdateDurations
                |> List.filter (fun duration -> duration > budget)

            if not (List.isEmpty animationOverruns) then
                let animationP95 = percentile 0.95 output.AnimationUpdateDurations
                let animationP99 = percentile 0.99 output.AnimationUpdateDurations
                let animationMax = output.AnimationUpdateDurations |> List.fold max TimeSpan.Zero

                Console.Error.WriteLine(
                    String.Concat(
                        "warning: page.animate updates exceeded the ",
                        budget.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        " ms budget for ",
                        output.Path,
                        ": overruns=",
                        animationOverruns.Length.ToString(CultureInfo.InvariantCulture),
                        ", p95_ms=",
                        animationP95.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        ", p99_ms=",
                        animationP99.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                        ", max_ms=",
                        animationMax.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture)
                    )
                )

    let writeCaptureOutput framesPerSecond (output: CaptureOutputResult) =
        Console.Out.WriteLine(String.Concat("written: ", output.Path))

        writePerformance framesPerSecond output
        writeWebPPerformance output
