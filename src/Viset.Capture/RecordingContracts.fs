namespace Viset

open System
open System.Threading
open System.Threading.Tasks

type internal StoredFrame =
    { Format: CompressedImageFormat
      Path: string }

type internal PendingFrame =
    | InMemory of CompressedFrame
    | OnDisk of StoredFrame

type internal IRecordingFramePipeline =
    inherit IDisposable

    abstract AddAsync: CompressedFrame * CancellationToken -> Task<int>

    abstract CompleteAsync: CancellationToken -> Task

    abstract ReadAsync: int * CancellationToken -> Task<CompressedFrame>

    abstract Count: int
    abstract SpilledFrameCount: int
