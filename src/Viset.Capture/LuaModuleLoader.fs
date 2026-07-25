namespace Viset

open System
open System.IO
open System.Threading.Tasks
open Lua

type internal CaptureModuleLoader(scriptDirectory: string) =
    let root = Path.GetFullPath scriptDirectory

    let comparison =
        if OperatingSystem.IsWindows() then
            StringComparison.OrdinalIgnoreCase
        else
            StringComparison.Ordinal

    let modulePath moduleName =
        if String.IsNullOrWhiteSpace moduleName then
            None
        else
            let relative =
                String.Concat(moduleName.Replace('.', Path.DirectorySeparatorChar), ".lua")

            let candidate = Path.GetFullPath(relative, root)

            let prefix =
                String.Concat(root.TrimEnd Path.DirectorySeparatorChar, Path.DirectorySeparatorChar)

            if candidate.StartsWith(prefix, comparison) then
                Some candidate
            else
                None

    interface ILuaModuleLoader with
        member _.Exists moduleName =
            modulePath moduleName |> Option.exists File.Exists

        member _.LoadAsync(moduleName, cancellationToken) =
            cancellationToken.ThrowIfCancellationRequested()

            match modulePath moduleName with
            | Some path when File.Exists path -> ValueTask<LuaModule>(new LuaModule(moduleName, File.ReadAllBytes path))
            | _ -> ValueTask.FromException<LuaModule>(LuaModuleNotFoundException moduleName)
