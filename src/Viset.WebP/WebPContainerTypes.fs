namespace Viset

type internal WebPChunkKind =
    | AnimationFrame
    | StillImage
    | Other

type internal WebPChunk =
    { Kind: WebPChunkKind
      Offset: int
      DataOffset: int
      DataSize: int
      AnimationDuration: int option
      AnimationDurationOffset: int option }

type internal WebPContainer = { Chunks: WebPChunk list }
