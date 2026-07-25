namespace Viset

open System
open System.Diagnostics
open System.Globalization

[<DebuggerDisplay("BrowserOrigin")>]
type BrowserOrigin =
    | ExplicitPath
    | EnvironmentVariable
    | ManagedCache
    | SystemDiscovery of name: string

    override origin.ToString() =
        match origin with
        | ExplicitPath -> "explicit path"
        | EnvironmentVariable -> "VISET_BROWSER"
        | ManagedCache -> "managed cache"
        | SystemDiscovery name -> String.Concat("system ", name)

type BrowserExecutable =
    { ExecutablePath: string
      Origin: BrowserOrigin
      Version: string }

    override browser.ToString() = browser.ExecutablePath
