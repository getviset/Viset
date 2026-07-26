namespace Viset

open System
open System.Buffers
open System.Globalization
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Threading

module internal BrowserDownload =
    let downloadAndVerifyAsync
        (platform: BrowserPlatformLock)
        (archivePath: string)
        (downloadTimeout: TimeSpan)
        (cancellationToken: CancellationToken)
        =
        task {
            use timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            timeoutCancellation.CancelAfter downloadTimeout

            try
                use httpClient = new HttpClient()
                httpClient.Timeout <- Timeout.InfiniteTimeSpan

                use! response =
                    httpClient.GetAsync(
                        platform.Url,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutCancellation.Token
                    )

                response.EnsureSuccessStatusCode() |> ignore
                use! source = response.Content.ReadAsStreamAsync timeoutCancellation.Token

                use destination =
                    new FileStream(
                        archivePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous ||| FileOptions.SequentialScan
                    )

                use digest = IncrementalHash.CreateHash HashAlgorithmName.SHA256
                let buffer = ArrayPool<byte>.Shared.Rent 81920

                try
                    let mutable complete = false

                    while not complete do
                        let! read =
                            source.ReadAsync(
                                buffer.AsMemory(0, buffer.Length),
                                timeoutCancellation.Token
                            )

                        if read = 0 then
                            complete <- true
                        else
                            digest.AppendData(buffer, 0, read)

                            do!
                                destination.WriteAsync(
                                    buffer.AsMemory(0, read),
                                    timeoutCancellation.Token
                                )
                finally
                    ArrayPool<byte>.Shared.Return buffer

                do! destination.FlushAsync timeoutCancellation.Token

                let actualDigest =
                    digest.GetHashAndReset()
                    |> Convert.ToHexString
                    |> fun value -> value.ToLowerInvariant()

                if not (String.Equals(actualDigest, platform.Sha256, StringComparison.Ordinal)) then
                    raise (
                        InvalidDataException(
                            String.Concat(
                                "Browser archive SHA-256 mismatch for ",
                                platform.RuntimeIdentifier,
                                ": expected ",
                                platform.Sha256,
                                ", received ",
                                actualDigest,
                                "."
                            )
                        )
                    )
            with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                raise (
                    TimeoutException(
                        String.Concat(
                            "Browser download exceeded ",
                            downloadTimeout.TotalMilliseconds.ToString(
                                "0",
                                CultureInfo.InvariantCulture
                            ),
                            " ms."
                        )
                    )
                )
        }
