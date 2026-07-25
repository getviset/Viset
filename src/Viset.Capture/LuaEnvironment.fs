namespace Viset

open System.Net.Http
open System.Threading
open Lua

module internal LuaEnvironment =
    open LuaTableHelpers

    let create
        (plan: CapturePlan)
        (planned: PlannedCapture)
        (activeCase: ActiveCase)
        (processes: LuaProcessRegistry)
        (httpClient: HttpClient)
        (cancellationToken: CancellationToken)
        =
        let processTable = LuaProcessBindings.create processes

        let httpTable = LuaHttpBindings.create httpClient

        let pageTables = LuaPageBindings.create activeCase

        let captureFunctions =
            LuaCaptureBindings.create plan planned activeCase cancellationToken

        let scriptTable = LuaTable()

        setValue scriptTable "directory" (LuaValue plan.ScriptDirectory)

        let visetTable = LuaTable()
        setValue visetTable "api_version" (LuaValue 1.0)

        setValue visetTable "context" (LuaValue(LuaValueConversion.caseContext plan planned))

        setValue visetTable "script" (LuaValue scriptTable)

        setValue visetTable "process" (LuaValue processTable)

        setValue visetTable "http" (LuaValue httpTable)

        setValue visetTable "page" (LuaValue pageTables.Page)

        setValue visetTable "emulation" (LuaValue pageTables.Emulation)

        setValue visetTable "snapshot" (LuaValue captureFunctions.Snapshot)

        setValue visetTable "__duration_ms" (LuaValue captureFunctions.Duration)

        setValue visetTable "__now_ms" (LuaValue captureFunctions.Now)

        setValue visetTable "__sleep_ms" (LuaValue captureFunctions.Sleep)

        setValue visetTable "__recording_create" (LuaValue captureFunctions.RecordingCreate)

        setValue visetTable "__recording_start" (LuaValue captureFunctions.RecordingStart)

        setValue visetTable "__recording_stop" (LuaValue captureFunctions.RecordingStop)

        setValue visetTable "__recording_active" (LuaValue captureFunctions.RecordingActive)

        visetTable
