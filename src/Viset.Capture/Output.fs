namespace Viset

module Output =
    let preflight plan = OutputSafety.preflight plan

    let write force captured = OutputWriter.write force captured
