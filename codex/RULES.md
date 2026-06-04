# Codex Project Rules

These rules apply to Codex work in this fork.

## Compatibility

- Keep the Unity plugin assembly identity stable unless the user explicitly asks for a breaking change:
  - Assembly name: `websocket-sharp`
  - Target framework: `net472`
  - Assembly version: `1.0.2.32832`
  - Public key token: `5660b08a1845a91e`
- Do not account for Windows 7 or Windows 8 compatibility.
- Do not run Unity compilation, Unity batchmode, or Unity builds unless the user explicitly asks for it.

## Tests

- Run the repository test suite after each completed code change:
  - `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release`
- Keep heavier stress coverage in `tests\WebSocketSharp.StressTests` and run it explicitly:
  - `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --filter TestCategory=Stress`
- Add or update NUnit tests before or together with behavior changes.
- Keep loopback tests local-only and independent of external network services.
- Keep active CCU stress separate from connection-storm stress. A CCU test may ramp clients up before asserting the concurrent session count; a connection-storm test must be named and logged as a distinct scenario.
- Stress tests must use bounded waits, exact payload accounting, deterministic loopback cleanup, and configurable load via environment variables where practical.

## Proofs

- Record every successful verification in `codex/proofs/test-runs.md`.
- Include the date/time, branch, command, result, and the behavior covered.
- If a test fails during development, fix the code or test before logging a proof.

## Git

- After a successful test milestone, commit and push the current branch to `origin`.
- Do not stage unrelated user changes.
- Keep commits small enough that a failed later change can be reverted without losing previous verified work.
