namespace Viset

open System

module Program =
    let private writeErrors errors =
        errors
        |> List.iter (fun message -> Console.Error.WriteLine($"error: {message}"))

    let private writePlan (plan: CapturePlan) =
        plan.Warnings
        |> List.iter (fun warning -> Console.Error.WriteLine($"warning: {warning}"))

        Console.Out.WriteLine($"output: {plan.OutputPath}")
        Console.Out.WriteLine($"captures: {plan.Captures.Length}")

        plan.Captures
        |> List.iter (fun capture ->
            let kind =
                match capture.Kind with
                | Still -> "still"
                | Animation _ -> "animation"

            Console.Out.WriteLine($"{kind}: {capture.OutputRelativePath}"))

    [<EntryPoint>]
    let main arguments =
        match Cli.parse Environment.CurrentDirectory arguments with
        | Error message ->
            writeErrors [ message ]
            2
        | Ok Help ->
            Console.Out.WriteLine Cli.usage
            0
        | Ok Version ->
            Console.Out.WriteLine Cli.versionText
            0
        | Ok BrowserInstall ->
            Console.Error.WriteLine("error: browser install is not available until browser support is implemented.")
            3
        | Ok(Capture request) ->
            match Matrix.plan request with
            | Error errors ->
                writeErrors errors
                2
            | Ok plan ->
                writePlan plan
                0
