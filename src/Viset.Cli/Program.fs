namespace Viset

open System

module Program =
    [<EntryPoint>]
    let main arguments =
        match Cli.parse Environment.CurrentDirectory arguments with
        | Error message ->
            CliOutput.writeErrors [ message ]
            2
        | Ok Help ->
            Console.Out.WriteLine Cli.usage
            0
        | Ok Version ->
            Console.Out.WriteLine Cli.versionText
            0
        | Ok(Init request) -> Commands.initializeProject request
        | Ok BrowserInstall -> Commands.installBrowser ()
        | Ok(Capture request) ->
            match CaptureScript.plan request with
            | Error errors ->
                CliOutput.writeErrors errors
                2
            | Ok plan -> Commands.capture plan
