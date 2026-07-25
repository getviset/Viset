namespace Viset

open System
open System.Globalization
open System.IO
open System.Threading.Tasks

module internal RecordingPipeline =
    [<Literal>]
    let LiveMemoryFrameLimit = 8

    let extension format =
        match format with
        | PngImage -> ".png"
        | JpegImage -> ".jpg"

    let path root prefix (index: int) format =
        Path.Combine(root, String.Concat(prefix, index.ToString("D8", CultureInfo.InvariantCulture), extension format))

    let writeAsync root prefix index (frame: CompressedFrame) cancellationToken =
        task {
            ArgumentNullException.ThrowIfNull frame.Bytes

            if frame.Bytes.Length = 0 then
                invalidArg (nameof frame) "Recorded frame bytes must not be empty."

            let outputPath = path root prefix index frame.Format
            do! File.WriteAllBytesAsync(outputPath, frame.Bytes, cancellationToken)

            return
                { Format = frame.Format
                  Path = outputPath }
        }

    let readAsync (stored: StoredFrame) cancellationToken =
        task {
            let! bytes = File.ReadAllBytesAsync(stored.Path, cancellationToken)

            return
                { Format = stored.Format
                  Bytes = bytes }
        }
