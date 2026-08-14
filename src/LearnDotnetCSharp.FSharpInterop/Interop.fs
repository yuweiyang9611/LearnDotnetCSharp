namespace LearnDotnetCSharp.FSharpInterop

module Analytics =
    [<CompiledName("MovingAverage")>]
    let movingAverage (window: int) (values: double array) =
        System.ArgumentNullException.ThrowIfNull(values)

        if window <= 0 then
            invalidArg (nameof window) "Window must be positive."

        values
        |> Array.windowed window
        |> Array.map Array.average

    [<CompiledName("Classify")>]
    let classify (value: int) =
        match value with
        | value when value < 0 -> "negative"
        | 0 -> "zero"
        | _ -> "positive"
