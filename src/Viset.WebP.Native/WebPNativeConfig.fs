namespace Viset

module internal WebPNativeConfig =
    let create (options: WebPNativeEncoderOptions) =
        let mutable config = Unchecked.defaultof<WebPConfig>

        WebPNativeLibraries.ensureLoaded ()
        WebPInteropLayouts.validate ()

        if
            WebPInterop.WebPConfigInitInternal(&config, 0, 75.0f, WebPInterop.EncoderAbiVersion) = 0
        then
            invalidOp "libwebp rejected the encoder ABI version."

        config.Lossless <- if options.Lossless then 1 else 0
        config.Quality <- options.Quality
        config.Method <- options.Method
        config.AlphaQuality <- 100
        config.Exact <- 1
        config.ThreadLevel <- 0

        if WebPInterop.WebPValidateConfig(&config) = 0 then
            invalidOp "libwebp rejected Viset's encoder configuration."

        config
