namespace Viset

open System
open System.Collections.Generic
open System.IO
open Viset.Serialization

module internal CaptureDeviceParser =
    open CaptureScriptPrimitives

    let resolveFrom directory fieldName value =
        match requiredText fieldName value with
        | Error errors -> Error errors
        | Ok path ->
            try
                Ok(Path.GetFullPath(path, directory))
            with
            | :? ArgumentException
            | :? NotSupportedException
            | :? PathTooLongException ->
                error (concat [| fieldName; " is not a valid path: "; path |])

    let parseFrameSource scriptDirectory value =
        if String.IsNullOrWhiteSpace value then
            Ok None
        elif String.Equals(value, "builtin:auto", StringComparison.OrdinalIgnoreCase) then
            Ok(Some(BuiltInFrame Automatic))
        elif String.Equals(value, "builtin:phone", StringComparison.OrdinalIgnoreCase) then
            Ok(Some(BuiltInFrame Phone))
        elif String.Equals(value, "builtin:laptop", StringComparison.OrdinalIgnoreCase) then
            Ok(Some(BuiltInFrame Laptop))
        elif value.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) then
            error (
                String.Concat(
                    "Unknown built-in frame '",
                    value,
                    "'; expected builtin:auto, builtin:phone, or builtin:laptop."
                )
            )
        else
            resolveFrom scriptDirectory "frame" value |> Result.map (CustomFrame >> Some)

    let private parseDimensions path (model: DimensionsTomlModel) =
        let parseDimension name (value: Nullable<int64>) =
            if not value.HasValue then
                error (String.Concat(appendKey path name, " is required."))
            elif value.Value <= 0L || value.Value > int64 Int32.MaxValue then
                error (
                    concat
                        [| appendKey path name
                           " must be between 1 and "
                           invariantInt32 Int32.MaxValue
                           "." |]
                )
            else
                Ok(int value.Value)

        match parseDimension "width" model.Width, parseDimension "height" model.Height with
        | Ok width, Ok height -> Ok { Width = width; Height = height }
        | Error errors, _ -> Error errors
        | _, Error errors -> Error errors

    let private parseDevice frameSource (name: string) (model: DeviceTomlModel) =
        if String.IsNullOrWhiteSpace name then
            error "Device names must not be empty."
        else
            let deviceScale = model.DeviceScale |> Option.ofNullable |> Option.defaultValue 1.0

            if not (Double.IsFinite deviceScale) || deviceScale <= 0.0 then
                error (
                    concat [| "devices."; name; ".device_scale must be a positive finite number." |]
                )
            else
                match
                    parseDimensions (concat [| "devices."; name; ".viewport" |]) model.Viewport
                with
                | Error errors -> Error errors
                | Ok viewport ->
                    let explicitFrameResult =
                        match Option.ofObj model.Frame with
                        | None -> Ok None
                        | Some frame ->
                            parseDimensions (concat [| "devices."; name; ".frame" |]) frame
                            |> Result.map Some

                    match explicitFrameResult with
                    | Error errors -> Error errors
                    | Ok explicitFrame ->
                        let device =
                            { Name = name
                              Mobile =
                                model.Mobile |> Option.ofNullable |> Option.defaultValue false
                              Touch = model.Touch |> Option.ofNullable |> Option.defaultValue false
                              DeviceScale = deviceScale
                              Viewport = viewport
                              Frame = explicitFrame }

                        match explicitFrame, frameSource with
                        | None, Some(BuiltInFrame style) ->
                            BuiltInFrames.deriveDimensions style device
                            |> Result.mapError List.singleton
                            |> Result.map (fun frame -> name, { device with Frame = Some frame })
                        | _ -> Ok(name, device)

    let parseDevices frameSource (devices: Dictionary<string, DeviceTomlModel>) =
        if devices.Count = 0 then
            error "devices is required and must contain at least one device."
        else
            devices
            |> Seq.map (fun entry -> entry.Key, entry.Value)
            |> List.ofSeq
            |> traverse (fun _ (name, model) -> parseDevice frameSource name model)
