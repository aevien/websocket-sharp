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
- Do not use global `ThreadPool.SetMinThreads` as a library fix for socket lifecycle starvation unless the user explicitly accepts that process-wide tradeoff.
- Server-side handshake timeouts must stay configurable and must be covered by slow or silent client tests before changing defaults.
- Resource lifecycle stress should separate CLR/process warm-up from steady-state assertions; assert session cleanup strictly and thread-count drift only after cooldown.
- Close lifecycle changes must cover graceful repeated close/dispose and abrupt TCP disconnect, with server sessions returning to zero.
- Protocol frame changes must include raw TCP tests for valid frame boundaries/fragmentation and malformed frames, with protocol-error sessions returning to zero.
- Close-frame validation changes must cover one-byte payloads, invalid or reserved close codes, invalid UTF-8 reasons, oversized control payloads, and non-minimal extended length encoding.

## Proofs

- Record every successful verification in `codex/proofs/test-runs.md`.
- Include the date/time, branch, command, result, and the behavior covered.
- If a test fails during development, fix the code or test before logging a proof.

## Git

- After a successful test milestone, commit and push the current branch to `origin`.
- Do not stage unrelated user changes.
- Keep commits small enough that a failed later change can be reverted without losing previous verified work.
