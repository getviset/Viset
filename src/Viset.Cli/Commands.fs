namespace Viset

open System
open System.Threading

module internal Commands =
    let installBrowser () =
        let sidecar = BrowserInstall.findBrowserLockSidecar AppContext.BaseDirectory

        match
            BrowserInstall.installAsync sidecar CancellationToken.None
            |> fun work -> work.GetAwaiter().GetResult()
        with
        | Error message ->
            CliOutput.writeErrors [ message ]
            3
        | Ok browser ->
            Console.Out.WriteLine(String.Concat("installed browser: ", browser.ExecutablePath))
            Console.Out.WriteLine(String.Concat("version: ", browser.Version))

            0

    let initializeProject request =
        match Scaffold.run request with
        | Error message ->
            CliOutput.writeErrors [ message ]
            1
        | Ok result ->
            Console.Out.WriteLine(String.Concat("initialized: ", result.DirectoryPath))
            Console.Out.WriteLine(String.Concat("next: viset capture ", result.CapturePath))

            0

    let capture (plan: CapturePlan) =
        use cancellation = new CancellationTokenSource()

        let cancelHandler =
            ConsoleCancelEventHandler(fun _ arguments ->
                arguments.Cancel <- true
                cancellation.Cancel())

        Console.CancelKeyPress.AddHandler cancelHandler

        try
            CliOutput.writePlan plan

            let sidecar = BrowserInstall.findBrowserLockSidecar AppContext.BaseDirectory

            match
                BrowserResolution.resolveAsync plan.BrowserPath sidecar cancellation.Token
                |> fun work -> work.GetAwaiter().GetResult()
            with
            | Error message ->
                CliOutput.writeErrors [ message ]
                3

            | Ok browser ->
                try
                    let result =
                        LuaHost.runAsync Cli.version plan browser cancellation.Token
                        |> fun work -> work.GetAwaiter().GetResult()

                    result.Outputs |> List.iter (CliOutput.writeCaptureOutput plan.FramesPerSecond)

                    0
                with
                | :? OperationCanceledException ->
                    CliOutput.writeErrors [ "Capture was cancelled." ]

                    130
                | error ->
                    CliOutput.writeErrors [ error.Message ]
                    1
        finally
            Console.CancelKeyPress.RemoveHandler cancelHandler
