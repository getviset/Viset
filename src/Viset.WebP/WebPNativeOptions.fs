namespace Viset

module internal WebPNativeOptions =
    let fromOptions (options: WebPOptions) =
        { Lossless =
            match options.Mode with
            | Lossy _ -> false
            | Lossless _ -> true
          Quality = float32 options.Mode.Quality
          Method = options.Method }
