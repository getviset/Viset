namespace Viset

open System
open System.Diagnostics
open StbImageSharp

module internal ImageDecoder =
    type DecodedFrame =
        { Width: int
          Height: int
          Rgba: byte array }

    let private decodeFrame (frame: CompressedFrame) =
        ArgumentNullException.ThrowIfNull frame.Bytes

        if frame.Bytes.Length = 0 then
            invalidArg (nameof frame) "Compressed image bytes must not be empty."

        let image = ImageResult.FromMemory(frame.Bytes, ColorComponents.RedGreenBlueAlpha)

        if
            image.Width <= 0
            || image.Width > WebPNativeDimensions.Maximum
            || image.Height <= 0
            || image.Height > WebPNativeDimensions.Maximum
        then
            invalidArg (nameof frame) "Animated WebP dimensions must be between 1 and 16383 pixels."

        { Width = image.Width
          Height = image.Height
          Rgba = image.Data }

    let decodeMeasured frame =
        let stopwatch = Stopwatch.StartNew()
        let decoded = decodeFrame frame
        stopwatch.Stop()
        decoded, stopwatch.Elapsed
