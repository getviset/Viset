namespace Viset

open System
open System.Diagnostics
open System.Globalization

[<DebuggerDisplay("CaptureFormat")>]
type CaptureFormat =
    | Png
    | WebP

    override format.ToString() =
        match format with
        | Png -> "png"
        | WebP -> "webp"

[<DebuggerDisplay("WebPSource")>]
type WebPSource =
    | PngScreencast
    | JpegScreencast of quality: int

    override source.ToString() =
        match source with
        | PngScreencast -> "png_screencast"
        | JpegScreencast _ -> "jpeg_screencast"

[<DebuggerDisplay("CompressedImageFormat")>]
type CompressedImageFormat =
    | PngImage
    | JpegImage

    override format.ToString() =
        match format with
        | PngImage -> "png"
        | JpegImage -> "jpeg"

type CompressedFrame =
    { Format: CompressedImageFormat
      Bytes: byte array }

    override frame.ToString() =
        String.Concat(
            frame.Format.ToString(),
            ":",
            frame.Bytes.Length.ToString(CultureInfo.InvariantCulture)
        )

[<DebuggerDisplay("WebPEncoder")>]
type WebPEncoder =
    | LibWebPFull
    | LibWebPAnim
    | Ffmpeg of executablePath: string

    override encoder.ToString() =
        match encoder with
        | LibWebPFull -> "libwebp_full"
        | LibWebPAnim -> "libwebp_anim"
        | Ffmpeg _ -> "ffmpeg"

[<DebuggerDisplay("WebPPipeline")>]
type WebPPipeline =
    | Spooled
    | Live

    override pipeline.ToString() =
        match pipeline with
        | Spooled -> "spooled"
        | Live -> "live"

[<DebuggerDisplay("WebPMode")>]
type WebPMode =
    | Lossy of quality: double
    | Lossless of effort: double

    member mode.Quality =
        match mode with
        | Lossy quality
        | Lossless quality -> quality

    override mode.ToString() =
        match mode with
        | Lossy _ -> "lossy"
        | Lossless _ -> "lossless"

type WebPOptions =
    { Source: WebPSource
      Encoder: WebPEncoder
      Pipeline: WebPPipeline
      Mode: WebPMode
      Method: int }

    static member Default =
        { Source = PngScreencast
          Encoder = LibWebPFull
          Pipeline = Spooled
          Mode = Lossy 75.0
          Method = 0 }

    override options.ToString() = options.Encoder.ToString()
