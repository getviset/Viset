namespace Viset

open System.Runtime.InteropServices

module internal WebPInterop =
    [<Literal>]
    let EncoderAbiVersion = 0x0210

    [<Literal>]
    let MuxAbiVersion = 0x0109

    [<Literal>]
    let MuxSuccess = 1

    [<Literal>]
    let AnimationFrameChunk = 3

    [<Literal>]
    let DisposeToBackground = 1

    [<Literal>]
    let NoBlend = 1

    [<Literal>]
    let CopyData = 1

    [<DllImport("libwebp",
                EntryPoint = "WebPConfigInitInternal",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPConfigInitInternal(
        WebPConfig& config,
        int preset,
        float32 quality,
        int abiVersion
    )

    [<DllImport("libwebp",
                EntryPoint = "WebPValidateConfig",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPValidateConfig(WebPConfig& config)

    [<DllImport("libwebp",
                EntryPoint = "WebPPictureInitInternal",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPPictureInitInternal(WebPPicture& picture, int abiVersion)

    [<DllImport("libwebp",
                EntryPoint = "WebPPictureImportRGBA",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPPictureImportRgba(WebPPicture& picture, nativeint rgba, int stride)

    [<DllImport("libwebp",
                EntryPoint = "WebPPictureFree",
                CallingConvention = CallingConvention.Cdecl)>]
    extern void WebPPictureFree(WebPPicture& picture)

    [<DllImport("libwebp",
                EntryPoint = "WebPMemoryWriterInit",
                CallingConvention = CallingConvention.Cdecl)>]
    extern void WebPMemoryWriterInit(nativeint writer)

    [<DllImport("libwebp",
                EntryPoint = "WebPMemoryWriterClear",
                CallingConvention = CallingConvention.Cdecl)>]
    extern void WebPMemoryWriterClear(nativeint writer)

    [<DllImport("libwebp", EntryPoint = "WebPEncode", CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPEncode(WebPConfig& config, WebPPicture& picture)

    [<DllImport("libwebp", EntryPoint = "WebPFree", CallingConvention = CallingConvention.Cdecl)>]
    extern void WebPFree(nativeint pointer)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPNewInternal",
                CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint WebPMuxNewInternal(int abiVersion)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPMuxSetCanvasSize",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPMuxSetCanvasSize(nativeint mux, int width, int height)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPMuxSetAnimationParams",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPMuxSetAnimationParameters(nativeint mux, WebPMuxAnimationParameters& parameters)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPMuxPushFrame",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPMuxPushFrame(nativeint mux, WebPMuxFrameInfo& frame, int copyData)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPMuxAssemble",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPMuxAssemble(nativeint mux, WebPData& data)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPMuxDelete",
                CallingConvention = CallingConvention.Cdecl)>]
    extern void WebPMuxDelete(nativeint mux)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderOptionsInitInternal",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPAnimEncoderOptionsInitInternal(WebPAnimEncoderOptions& options, int abiVersion)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderNewInternal",
                CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint WebPAnimEncoderNewInternal(
        int width,
        int height,
        WebPAnimEncoderOptions& options,
        int abiVersion
    )

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderAdd",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPAnimEncoderAdd(
        nativeint encoder,
        WebPPicture& picture,
        int timestampMilliseconds,
        WebPConfig& config
    )

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderAdd",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPAnimEncoderFinish(
        nativeint encoder,
        nativeint picture,
        int timestampMilliseconds,
        nativeint config
    )

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderAssemble",
                CallingConvention = CallingConvention.Cdecl)>]
    extern int WebPAnimEncoderAssemble(nativeint encoder, WebPData& data)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderGetError",
                CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint WebPAnimEncoderGetError(nativeint encoder)

    [<DllImport("libwebpmux",
                EntryPoint = "WebPAnimEncoderDelete",
                CallingConvention = CallingConvention.Cdecl)>]
    extern void WebPAnimEncoderDelete(nativeint encoder)
