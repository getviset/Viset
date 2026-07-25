namespace Viset

open System
open System.Globalization
open System.Runtime.InteropServices

module internal WebPNativeData =
    let checkMux operation result =
        if result <> WebPInterop.MuxSuccess then
            invalidOp (
                String.Format(CultureInfo.InvariantCulture, "{0} failed with libwebpmux result {1}.", operation, result)
            )

    let private readWriterSize writer =
        if IntPtr.Size = 8 then
            Marshal.ReadInt64(writer, IntPtr.Size) |> uint64
        else
            Marshal.ReadInt32(writer, IntPtr.Size) |> uint32 |> uint64

    let copyMemoryWriter writer =
        let pointer = Marshal.ReadIntPtr writer
        let size = readWriterSize writer

        if pointer = 0n || size = 0UL then
            invalidOp "libwebp encoded an empty animation frame."

        if size > uint64 Int32.MaxValue then
            invalidOp "A WebP frame exceeds Viset's managed output size limit."

        let output = Array.zeroCreate<byte> (int size)
        Marshal.Copy(pointer, output, 0, output.Length)
        output

    let copy operation (data: WebPData) =
        let size = uint64 data.Size

        if size = 0UL then
            invalidOp (String.Concat(operation, " returned empty output."))

        if size > uint64 Int32.MaxValue then
            invalidOp "Animated WebP output exceeds Viset's managed output size limit."

        let output = Array.zeroCreate<byte> (int size)
        Marshal.Copy(data.Bytes, output, 0, output.Length)
        output
