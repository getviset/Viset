namespace Viset

open System.Runtime.InteropServices

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal WebPConfig =
    val mutable Lossless: int
    val mutable Quality: float32
    val mutable Method: int
    val mutable ImageHint: int
    val mutable TargetSize: int
    val mutable TargetPsnr: float32
    val mutable Segments: int
    val mutable SnsStrength: int
    val mutable FilterStrength: int
    val mutable FilterSharpness: int
    val mutable FilterType: int
    val mutable AutoFilter: int
    val mutable AlphaCompression: int
    val mutable AlphaFiltering: int
    val mutable AlphaQuality: int
    val mutable Pass: int
    val mutable ShowCompressed: int
    val mutable Preprocessing: int
    val mutable Partitions: int
    val mutable PartitionLimit: int
    val mutable EmulateJpegSize: int
    val mutable ThreadLevel: int
    val mutable LowMemory: int
    val mutable NearLossless: int
    val mutable Exact: int
    val mutable UseDeltaPalette: int
    val mutable UseSharpYuv: int
    val mutable MinimumQuality: int
    val mutable MaximumQuality: int

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal WebPPicture =
    val mutable UseArgb: int
    val mutable Colorspace: int
    val mutable Width: int
    val mutable Height: int
    val mutable Y: nativeint
    val mutable U: nativeint
    val mutable V: nativeint
    val mutable YStride: int
    val mutable UvStride: int
    val mutable A: nativeint
    val mutable AStride: int
    val mutable Padding1A: uint32
    val mutable Padding1B: uint32
    val mutable Argb: nativeint
    val mutable ArgbStride: int
    val mutable Padding2A: uint32
    val mutable Padding2B: uint32
    val mutable Padding2C: uint32
    val mutable Writer: nativeint
    val mutable CustomPointer: nativeint
    val mutable ExtraInfoType: int
    val mutable ExtraInfo: nativeint
    val mutable Statistics: nativeint
    val mutable ErrorCode: int
    val mutable ProgressHook: nativeint
    val mutable UserData: nativeint
    val mutable Padding3A: uint32
    val mutable Padding3B: uint32
    val mutable Padding3C: uint32
    val mutable Padding4: nativeint
    val mutable Padding5: nativeint
    val mutable Padding6A: uint32
    val mutable Padding6B: uint32
    val mutable Padding6C: uint32
    val mutable Padding6D: uint32
    val mutable Padding6E: uint32
    val mutable Padding6F: uint32
    val mutable Padding6G: uint32
    val mutable Padding6H: uint32
    val mutable Memory: nativeint
    val mutable MemoryArgb: nativeint
    val mutable Padding7A: nativeint
    val mutable Padding7B: nativeint

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal WebPMuxAnimationParameters =
    val mutable BackgroundColor: uint32
    val mutable LoopCount: int

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal WebPAnimEncoderOptions =
    val mutable AnimationParameters: WebPMuxAnimationParameters
    val mutable MinimizeSize: int
    val mutable KeyFrameMinimum: int
    val mutable KeyFrameMaximum: int
    val mutable AllowMixed: int
    val mutable Verbose: int
    val mutable PaddingA: uint32
    val mutable PaddingB: uint32
    val mutable PaddingC: uint32
    val mutable PaddingD: uint32

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal WebPData =
    val mutable Bytes: nativeint
    val mutable Size: unativeint

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal WebPMuxFrameInfo =
    val mutable Bitstream: WebPData
    val mutable XOffset: int
    val mutable YOffset: int
    val mutable Duration: int
    val mutable ChunkId: int
    val mutable DisposeMethod: int
    val mutable BlendMethod: int
    val mutable Padding: uint32
