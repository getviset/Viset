namespace Viset

open System
open System.Globalization
open System.Threading

module Program =
    let private writeErrors errors =
        errors
        |> List.iter (fun message -> Console.Error.WriteLine(String.Concat("error: ", message)))

    let private writePlan (plan: CapturePlan) =
        plan.Warnings
        |> List.iter (fun warning -> Console.Error.WriteLine(String.Concat("warning: ", warning)))

        Console.Out.WriteLine(String.Concat("output: ", plan.OutputPath))

        Console.Out.WriteLine(String.Concat("captures: ", plan.Captures.Length.ToString(CultureInfo.InvariantCulture)))

        plan.Captures
        |> List.iter (fun capture ->
            let kind =
                match capture.Kind with
                | Still -> "still"
                | Animation _ -> "animation"

            Console.Out.WriteLine(String.Concat(kind, ": ", capture.OutputRelativePath)))

    let private installBrowser () =
        match BrowserInstall.locateBrowserLock AppContext.BaseDirectory Environment.CurrentDirectory with
        | Error message ->
            writeErrors [ message ]
            3
        | Ok lockPath ->
            match
                BrowserInstall.installAsync lockPath CancellationToken.None
                |> fun work -> work.GetAwaiter().GetResult()
            with
            | Error message ->
                writeErrors [ message ]
                3
            | Ok browser ->
                Console.Out.WriteLine(String.Concat("installed browser: ", browser.ExecutablePath))
                Console.Out.WriteLine(String.Concat("version: ", browser.Version))
                0

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
        | Ok BrowserInstall -> installBrowser ()
        | Ok(Capture request) ->
            match Matrix.plan request with
            | Error errors ->
                writeErrors errors
                2
            | Ok plan ->
                writePlan plan
                0
