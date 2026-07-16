namespace Viset

type TomlValue =
    | String of string
    | Integer of int64
    | Float of double
    | Boolean of bool
    | DateTime of string
    | Array of TomlValue list
    | Table of (string * TomlValue) list

type Dimensions = { Width: int; Height: int }

type Device =
    { Name: string
      Mobile: bool
      Touch: bool
      DeviceScale: double
      Viewport: Dimensions
      Frame: Dimensions option }

type CaptureKind =
    | Still
    | Animation of workflow: string

type MatrixDefinition =
    { Id: string
      NameTemplate: string
      Kind: CaptureKind
      Axes: (string * TomlValue list) list
      Data: (string * TomlValue) list }

type CaptureRequest =
    { MatrixPath: string
      OutputPath: string option
      OnlyDefinitionId: string option
      BrowserPath: string option }

type Command =
    | Capture of CaptureRequest
    | BrowserInstall
    | Help
    | Version

type PlannedCapture =
    { DefinitionId: string
      Kind: CaptureKind
      LogicalName: string
      OutputRelativePath: string
      Device: Device
      Axes: (string * TomlValue) list
      Data: (string * TomlValue) list }

type CapturePlan =
    { MatrixPath: string
      OutputPath: string
      AdapterPath: string
      FramePath: string option
      BrowserPath: string option
      BrowserArguments: string list
      FramesPerSecond: int
      Captures: PlannedCapture list
      Warnings: string list }
