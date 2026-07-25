namespace Viset

open System
open System.Text

type private ScaffoldAssetMarker = class end

module internal ScaffoldAssets =
    let defaultPage =
        EmbeddedText.read<ScaffoldAssetMarker> "Viset.Scaffold.DefaultPage.html"

    let captureTemplate =
        EmbeddedText.read<ScaffoldAssetMarker> "Viset.Scaffold.Capture.lua"

    let readmeTemplate =
        EmbeddedText.read<ScaffoldAssetMarker> "Viset.Scaffold.Readme.md"

    let defaultPageUrl =
        let payload = defaultPage |> Encoding.UTF8.GetBytes |> Convert.ToBase64String

        String.Concat("data:text/html;charset=utf-8;base64,", payload)
