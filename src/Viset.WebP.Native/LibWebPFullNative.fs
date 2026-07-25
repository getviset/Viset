namespace Viset

open System
open System.Globalization
open System.Runtime.InteropServices

module internal LibWebPFullNative =
    type Encoder =
        private
            { Handle: nativeint
              Width: int
              Height: int
              Config: WebPConfig
              MemoryWriter: nativeint }

    let create options width height =
        WebPNativeDimensions.validate width height
        let config = WebPNativeConfig.create options
        let handle = WebPInterop.WebPMuxNewInternal WebPInterop.MuxAbiVersion

        if handle = 0n then
            invalidOp "libwebpmux could not create an animation muxer."

        try
            WebPInterop.WebPMuxSetCanvasSize(handle, width, height)
            |> WebPNativeData.checkMux "Setting the animated WebP canvas"

            let mutable parameters = Unchecked.defaultof<WebPMuxAnimationParameters>
            parameters.LoopCount <- 0
            parameters.BackgroundColor <- 0u

            WebPInterop.WebPMuxSetAnimationParameters(handle, &parameters)
            |> WebPNativeData.checkMux "Setting the animated WebP parameters"

            { Handle = handle
              Width = width
              Height = height
              Config = config
              MemoryWriter = WebPNativeLibraries.webPMemoryWriter () }
        with _ ->
            WebPInterop.WebPMuxDelete handle
            reraise ()

    let encodeFrame (encoder: Encoder) (rgba: byte array) =
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
            let writer = Marshal.AllocHGlobal(if IntPtr.Size = 8 then 32 else 16)
            let mutable writerInitialized = false

            try
                WebPInterop.WebPMemoryWriterInit writer
                writerInitialized <- true
                picture.Writer <- encoder.MemoryWriter
                picture.CustomPointer <- writer

                let pixels = GCHandle.Alloc(rgba, GCHandleType.Pinned)

                try
                    if
                        WebPInterop.WebPPictureImportRgba(&picture, pixels.AddrOfPinnedObject(), encoder.Width * 4) = 0
                    then
                        invalidOp "libwebp could not import an RGBA animation frame."

                    let mutable config = encoder.Config

                    if WebPInterop.WebPEncode(&config, &picture) = 0 then
                        invalidOp (
                            String.Format(
                                CultureInfo.InvariantCulture,
                                "libwebp could not encode an animation frame (error {0}).",
                                picture.ErrorCode
                            )
                        )

                    WebPNativeData.copyMemoryWriter writer
                finally
                    pixels.Free()
            finally
                if writerInitialized then
                    WebPInterop.WebPMemoryWriterClear writer

                Marshal.FreeHGlobal writer
        finally
            WebPInterop.WebPPictureFree(&picture)

    let addFrame (encoder: Encoder) duration (encoded: byte array) =
        let bytes = GCHandle.Alloc(encoded, GCHandleType.Pinned)

        try
            let mutable frame = Unchecked.defaultof<WebPMuxFrameInfo>
            frame.Bitstream.Bytes <- bytes.AddrOfPinnedObject()
            frame.Bitstream.Size <- unativeint encoded.Length
            frame.Duration <- duration
            frame.ChunkId <- WebPInterop.AnimationFrameChunk
            frame.DisposeMethod <- WebPInterop.DisposeToBackground
            frame.BlendMethod <- WebPInterop.NoBlend

            WebPInterop.WebPMuxPushFrame(encoder.Handle, &frame, WebPInterop.CopyData)
            |> WebPNativeData.checkMux "Adding an animated WebP frame"
        finally
            bytes.Free()

    let assemble (encoder: Encoder) =
        let mutable data = Unchecked.defaultof<WebPData>

        try
            WebPInterop.WebPMuxAssemble(encoder.Handle, &data)
            |> WebPNativeData.checkMux "Assembling the animated WebP"

            WebPNativeData.copy "libwebpmux" data
        finally
            if data.Bytes <> 0n then
                WebPInterop.WebPFree data.Bytes

    let dispose (encoder: Encoder) =
        WebPInterop.WebPMuxDelete encoder.Handle

    let dimensions (encoder: Encoder) = encoder.Width, encoder.Height
