namespace Viset

open System
open System.Collections.Generic

type internal BrowserPlatformLock =
    { RuntimeIdentifier: string
      Url: Uri
      Sha256: string
      ExecutableLayout: string }

    override platform.ToString() = platform.RuntimeIdentifier

type internal BrowserLock =
    { LockPath: string
      BrowserVersion: string
      Revision: string
      Platforms: IReadOnlyDictionary<string, BrowserPlatformLock> }

    override browserLock.ToString() = browserLock.BrowserVersion
