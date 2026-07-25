namespace Viset

module internal WebPNativeDimensions =
    [<Literal>]
    let Maximum = 16383

    let validate width height =
        if width <= 0 || width > Maximum || height <= 0 || height > Maximum then
            invalidArg (nameof width) "Animated WebP dimensions must be between 1 and 16383 pixels."
