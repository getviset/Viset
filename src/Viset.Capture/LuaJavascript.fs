namespace Viset

open System.Globalization
open Lua

type private AssemblyMarker = class end

module internal LuaJavascript =
    let private evaluateRunner =
        EmbeddedText.read<AssemblyMarker> "Viset.EvaluateRunner.js"

    let private animationRunner =
        EmbeddedText.read<AssemblyMarker> "Viset.AnimationRunner.js"

    let evaluateExpression (script: string) (arguments: LuaTable) =
        let argumentExpression = LuaJavascriptArguments.expression arguments

        $"""
        {evaluateRunner}(
          ({script}),
          {argumentExpression}
        )
        """

    let animationExpression (duration: double) (update: string) (easing: string) =
        let easingExpression =
            match easing with
            | "linear" -> "progress => progress"

            | "in_sine" -> "progress => 1 - Math.cos((progress * Math.PI) / 2)"

            | "out_sine" -> "progress => Math.sin((progress * Math.PI) / 2)"

            | "in_out_sine" -> "progress => -(Math.cos(Math.PI * progress) - 1) / 2"

            | custom -> custom

        let durationText = duration.ToString("R", CultureInfo.InvariantCulture)

        $"""
        {animationRunner}(
          {durationText},
          ({update}),
          ({easingExpression})
        )
        """
