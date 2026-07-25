namespace Viset

open System
open System.Runtime.InteropServices

module internal LibWebPAnimNative =
    type Encoder =
        private
            { Handle: nativeint
              Width: int
              Height: int
              Config: WebPConfig }

    let private encoderError encoder operation =
        let pointer = WebPInterop.WebPAnimEncoderGetError encoder

        let reason =
            if pointer = 0n then
                "unknown error"
            else
                Marshal.PtrToStringUTF8 pointer
                |> Option.ofObj
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
                |> Option.defaultValue "unknown error"

        invalidOp (String.Concat(operation, " failed: ", reason))

    let create options width height =
        WebPNativeDimensions.validate width height
        let config = WebPNativeConfig.create options
        let mutable encoderOptions = Unchecked.defaultof<WebPAnimEncoderOptions>

        if WebPInterop.WebPAnimEncoderOptionsInitInternal(&encoderOptions, WebPInterop.MuxAbiVersion) = 0 then
            invalidOp "libwebp_anim rejected the mux ABI version."

        encoderOptions.AnimationParameters.LoopCount <- 0
        encoderOptions.AnimationParameters.BackgroundColor <- 0u

        let handle =
            WebPInterop.WebPAnimEncoderNewInternal(width, height, &encoderOptions, WebPInterop.MuxAbiVersion)

        if handle = 0n then
            invalidOp "libwebp_anim could not create an animation encoder."

        { Handle = handle
          Width = width
          Height = height
          Config = config }

    let addFrame (encoder: Encoder) timestampMilliseconds (rgba: byte array) =
        ArgumentNullException.ThrowIfNull rgba

        let expectedLength = int64 encoder.Width * int64 encoder.Height * 4L

        if rgba.LongLength <> expectedLength then
            invalidArg (nameof rgba) "RGBA frame data does not match the animation dimensions."

        let mutable picture = Unchecked.defaultof<WebPPicture>

        if WebPInterop.WebPPictureInitInternal(&picture, WebPInterop.EncoderAbiVersion) = 0 then
            invalidOp "libwebp rejected the picture ABI version."

        picture.Width <- encoder.Width
        picture.Height <- encoder.Height
        picture.UseArgb <- 1

        try
            let pixels = GCHandle.Alloc(rgba, GCHandleType.Pinned)

            try
                if WebPInterop.WebPPictureImportRgba(&picture, pixels.AddrOfPinnedObject(), encoder.Width * 4) = 0 then
                    invalidOp "libwebp could not import an RGBA animation frame."

                let mutable config = encoder.Config

                if WebPInterop.WebPAnimEncoderAdd(encoder.Handle, &picture, timestampMilliseconds, &config) = 0 then
                    encoderError encoder.Handle "Adding a libwebp_anim frame"
            finally
                pixels.Free()
        finally
            WebPInterop.WebPPictureFree(&picture)

    let assemble (encoder: Encoder) finalTimestampMilliseconds =
        if WebPInterop.WebPAnimEncoderFinish(encoder.Handle, 0n, finalTimestampMilliseconds, 0n) = 0 then
            encoderError encoder.Handle "Finalizing libwebp_anim timestamps"

        let mutable data = Unchecked.defaultof<WebPData>

        try
            if WebPInterop.WebPAnimEncoderAssemble(encoder.Handle, &data) = 0 then
                encoderError encoder.Handle "Assembling libwebp_anim output"

            WebPNativeData.copy "libwebp_anim" data
        finally
            if data.Bytes <> 0n then
                WebPInterop.WebPFree data.Bytes

    let dispose (encoder: Encoder) =
        WebPInterop.WebPAnimEncoderDelete encoder.Handle

    let dimensions (encoder: Encoder) = encoder.Width, encoder.Height
