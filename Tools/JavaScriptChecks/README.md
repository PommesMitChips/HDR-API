# Workshop JavaScript prototype checks

This isolated gate certifies the selected JavaScript interpreter and its parser and
host source as one Workshop-mod candidate. It does not change the HTML release
gate or grant the candidate any analyzer permissions.

`Check.ps1` stages every selected `.cs` file, fingerprints the inputs, and invokes
`Tools/HtmlChecks/Whitelist/Check.ps1` unchanged. That checker uses the installed
Space Engineers default ModApi whitelist, C# 6, and the exact official
compatibility header. Its positive, unused-reflection, ambiguity, and pragma
controls remain mandatory. Candidate code is never executed in that host.

Interpreter behavior is tested separately in a CPU-only process with an external
wall-clock deadline. Internal execution limits must still stop loops and recursive
scripts; the external deadline is a test-runner backstop, not the mod's scheduler.
No game session, native render context, plugin, installation directory, or Pulsar
configuration is accessed by the runtime fixtures.

The runner records every source hash and shipped third-party license hash. It
refuses a package whose license inventory is absent or whose selected sources
change during the run. Passing the gate proves the selected interpreter profile,
not complete browser or modern ECMAScript support.

Outputs are restricted to `artifacts/js-prototype`. Prototype work remains
unaccepted until both the authentic ModApi gate and behavioral tests pass.
