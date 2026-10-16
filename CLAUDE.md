# CLAUDE.md

This file guides Claude Code in this repository. The full project guide
(architecture, commands, gotchas, conventions) lives in
[`AGENTS.md`](AGENTS.md) and applies here too — read it first.

## Claude-specific notes

- Follow the repo's zero-warning policy (`Directory.Build.props`,
  `TreatWarningsAsErrors=true`): run `dotnet build` and treat any warning as
  a build failure to fix, not suppress.
- Write commit messages and PR descriptions in German, per
  [`AGENTS.md`](AGENTS.md#conventions) — this repo's existing history is in
  German and new changes should match it.
- Run `dotnet test` before considering a change done; the suite is fast
  (pure logic, no MQTT/KNX I/O).
