namespace Viset

open System
open System.Globalization

type internal ScaffoldSettings =
    { PageUrl: string
      OutputPath: string
      ViewportWidth: int
      ViewportHeight: int }

    override settings.ToString() = settings.OutputPath

module internal ScaffoldInput =
    let private defaults =
        { PageUrl = ScaffoldAssets.defaultPageUrl
          OutputPath = "output/example.png"
          ViewportWidth = 1280
          ViewportHeight = 720 }

    let private prompt
        label
        displayedDefault
        defaultValue
        (validator: string -> Result<'value, string>)
        =
        let rec read () =
            Console.Out.Write($"{label} [{displayedDefault}]: ")

            Console.Out.Flush()

            match Console.ReadLine() with
            | null -> Error "Interactive input ended before initialization completed."
            | value ->
                let candidate =
                    if String.IsNullOrWhiteSpace(value) then
                        defaultValue
                    else
                        value.Trim()

                match validator candidate with
                | Ok result -> Ok result
                | Error message ->
                    Console.Error.WriteLine($"error: {message}")

                    read ()

        read ()

    let private interactiveSettings () =
        match
            prompt
                "Page URL"
                "built-in page"
                defaults.PageUrl
                ScaffoldValidation.validateAbsoluteUrl
        with
        | Error message -> Error message
        | Ok pageUrl ->
            match
                prompt
                    "Output file"
                    defaults.OutputPath
                    defaults.OutputPath
                    ScaffoldValidation.validateOutputPath
            with
            | Error message -> Error message
            | Ok outputPath ->
                let width = defaults.ViewportWidth.ToString(CultureInfo.InvariantCulture)

                match
                    prompt
                        "Viewport width"
                        width
                        width
                        (ScaffoldValidation.validateDimension "Viewport width")
                with
                | Error message -> Error message
                | Ok viewportWidth ->
                    let height = defaults.ViewportHeight.ToString(CultureInfo.InvariantCulture)

                    match
                        prompt
                            "Viewport height"
                            height
                            height
                            (ScaffoldValidation.validateDimension "Viewport height")
                    with
                    | Error message -> Error message
                    | Ok viewportHeight ->
                        Ok
                            { PageUrl = pageUrl
                              OutputPath = outputPath
                              ViewportWidth = viewportWidth
                              ViewportHeight = viewportHeight }

    let settings interactive =
        if interactive then interactiveSettings () else Ok defaults
