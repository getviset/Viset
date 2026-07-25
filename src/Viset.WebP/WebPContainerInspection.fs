namespace Viset

module internal WebPContainerInspection =
    let frameCount container =
        let animationFrames =
            container.Chunks
            |> List.sumBy (fun chunk -> if chunk.Kind = AnimationFrame then 1 else 0)

        if animationFrames > 0 then
            animationFrames
        elif container.Chunks |> List.exists (fun chunk -> chunk.Kind = StillImage) then
            1
        else
            invalidOp "An encoder returned a WebP container without an image frame."
