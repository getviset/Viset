namespace Viset

open System
open System.Text
open System.Text.Encodings.Web

module internal FrameBootstrap =
    let private utf8 = UTF8Encoding false

    let private javascriptString value =
        String.Concat("\"", JavaScriptEncoder.Default.Encode value, "\"")

    let script token (device: Device) =
        let imagePath = String.Concat("/", token, "/image")
        let builder = StringBuilder()
        builder.Append "(() => {\n" |> ignore
        builder.Append "const subscribers = new Set();\n" |> ignore
        builder.Append "let generation = 0;\n" |> ignore
        builder.Append "const device = Object.freeze({" |> ignore

        builder.Append("name:").Append(javascriptString device.Name).Append ','
        |> ignore

        builder.Append("mobile:").Append(if device.Mobile then "true" else "false").Append ','
        |> ignore

        builder.Append("touch:").Append(if device.Touch then "true" else "false").Append ','
        |> ignore

        builder
            .Append("device_scale:")
            .Append(device.DeviceScale.ToString("R", Globalization.CultureInfo.InvariantCulture))
            .Append
            ','
        |> ignore

        builder.Append("viewport_width:").Append(device.Viewport.Width).Append ','
        |> ignore

        builder.Append("viewport_height:").Append(device.Viewport.Height).Append ','
        |> ignore

        match device.Frame with
        | Some frame ->
            builder.Append("frame_width:").Append(frame.Width).Append ',' |> ignore
            builder.Append("frame_height:").Append frame.Height |> ignore
        | None ->
            builder.Append "frame_width:null," |> ignore
            builder.Append "frame_height:null" |> ignore

        builder.Append "});\n" |> ignore

        builder.Append "const snapshot = () => Object.freeze({generation,device,image_url:"
        |> ignore

        builder.Append(javascriptString imagePath).Append " + '?generation=' + generation});\n"
        |> ignore

        builder.Append "const notify = async () => {const value=snapshot();" |> ignore

        builder.Append "await Promise.all(Array.from(subscribers, callback => callback(value))); "
        |> ignore

        builder.Append "window.dispatchEvent(new CustomEvent('viset-frame-update',{detail:value})); return value;};\n"
        |> ignore

        builder.Append "window.visetFrame = Object.freeze({device,get current(){return snapshot();},"
        |> ignore

        builder.Append
            "subscribe(callback){if(typeof callback!=='function'){throw new TypeError('callback must be a function');}"
        |> ignore

        builder.Append
            "subscribers.add(callback); Promise.resolve(callback(snapshot())); return () => subscribers.delete(callback);},"
        |> ignore

        builder.Append "async update(){generation += 1; return await notify();}});\n"
        |> ignore

        builder.Append "window.addEventListener('DOMContentLoaded', () => {window.dispatchEvent("
        |> ignore

        builder.Append "new CustomEvent('viset-frame-ready',{detail:window.visetFrame.current}));}, {once:true});\n"
        |> ignore

        builder.Append "})();\n" |> ignore
        utf8.GetBytes(builder.ToString())

    let inject token (html: string) =
        let scriptTag = $"""<script src="/{token}/viset-frame.js"></script>"""

        let openingHead = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase)

        if openingHead >= 0 then
            let closingBracket = html.IndexOf('>', openingHead)

            if closingBracket >= 0 then
                html.Insert(closingBracket + 1, scriptTag)
            else
                String.Concat(scriptTag, html)
        else
            String.Concat(scriptTag, html)
