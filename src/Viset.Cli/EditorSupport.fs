namespace Viset

type private AssemblyMarker = class end

module EditorSupport =
    let LuaLanguageServerConfiguration =
        EmbeddedText.read<AssemblyMarker> "Viset.EditorSupport.luarc.json"

    let LuaDefinitions =
        EmbeddedText.read<AssemblyMarker> "Viset.EditorSupport.viset.d.lua"
