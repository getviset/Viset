namespace Viset

open System
open System.Collections.Generic

type BrowserSessionException(message: string, innerException: Exception) =
    inherit Exception(message, innerException)

type BrowserSessionOptions
    (executablePath: string, browserArguments: IReadOnlyList<string>, startupTimeout: TimeSpan, commandTimeout: TimeSpan)
    =
    do
        ArgumentException.ThrowIfNullOrWhiteSpace executablePath
        ArgumentNullException.ThrowIfNull browserArguments

        if startupTimeout <= TimeSpan.Zero then
            invalidArg (nameof startupTimeout) "Browser startup timeout must be positive."

        if commandTimeout <= TimeSpan.Zero then
            invalidArg (nameof commandTimeout) "CDP command timeout must be positive."

    member _.ExecutablePath = executablePath
    member _.BrowserArguments = browserArguments
    member _.StartupTimeout = startupTimeout
    member _.CommandTimeout = commandTimeout

    override _.ToString() = executablePath

    new(executablePath: string, browserArguments: IReadOnlyList<string>) =
        BrowserSessionOptions(executablePath, browserArguments, TimeSpan.FromSeconds 10.0, TimeSpan.FromSeconds 10.0)
