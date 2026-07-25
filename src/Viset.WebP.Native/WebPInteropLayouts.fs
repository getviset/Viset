namespace Viset

open System
open System.Globalization
open System.Runtime.InteropServices

module internal WebPInteropLayouts =
    let validate () =
        let expectedPictureSize = if IntPtr.Size = 8 then 256 else 172
        let expectedFrameInfoSize = if IntPtr.Size = 8 then 48 else 36

        let layouts =
            [ "WebPConfig", Marshal.SizeOf<WebPConfig>(), 116
              "WebPPicture", Marshal.SizeOf<WebPPicture>(), expectedPictureSize
              "WebPData", Marshal.SizeOf<WebPData>(), IntPtr.Size * 2
              "WebPMuxFrameInfo", Marshal.SizeOf<WebPMuxFrameInfo>(), expectedFrameInfoSize
              "WebPAnimEncoderOptions", Marshal.SizeOf<WebPAnimEncoderOptions>(), 44 ]

        layouts
        |> List.iter (fun (name, actual, expected) ->
            if actual <> expected then
                invalidOp (
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "The {0} interop layout is {1} bytes; libwebp requires {2} bytes.",
                        name,
                        actual,
                        expected
                    )
                ))
