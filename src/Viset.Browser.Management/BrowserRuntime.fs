namespace Viset

open System
open System.Runtime.InteropServices

module internal BrowserRuntime =
    let currentRuntimeIdentifier () =
        let architecture = RuntimeInformation.ProcessArchitecture

        if OperatingSystem.IsLinux() then
            match architecture with
            | Architecture.X64 -> "linux-x64"
            | Architecture.Arm64 -> "linux-arm64"
            | _ -> String.Concat("linux-", architecture.ToString().ToLowerInvariant())
        elif OperatingSystem.IsWindows() then
            match architecture with
            | Architecture.X64 -> "win-x64"
            | Architecture.Arm64 -> "win-arm64"
            | _ -> String.Concat("win-", architecture.ToString().ToLowerInvariant())
        elif OperatingSystem.IsMacOS() then
            match architecture with
            | Architecture.Arm64 -> "osx-arm64"
            | Architecture.X64 -> "osx-x64"
            | _ -> String.Concat("osx-", architecture.ToString().ToLowerInvariant())
        else
            RuntimeInformation.RuntimeIdentifier
