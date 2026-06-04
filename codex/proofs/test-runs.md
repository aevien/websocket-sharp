# Test Run Proofs

## 2026-06-04 - net472 baseline and NUnit loopback suite

- Branch: `codex/unity-compat-baseline`
- Command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Result: Passed, 5 total, 0 failed
- Covered:
  - Unity plugin assembly identity
  - Text echo over loopback WebSocket
  - Binary echo over loopback WebSocket
  - Ping on open connection
  - Origin validator rejection

## 2026-06-04 - TLS certificate validation defaults

- Branch: `codex/unity-compat-baseline`
- Command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release`
- Control command before push: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Result: Passed, 13 total, 0 failed
- Covered:
  - Client default server certificate validation accepts only `SslPolicyErrors.None`
  - Client default server certificate validation rejects certificate policy errors
  - Client custom server certificate validation callback remains user-controlled
  - Client SSL configuration copy preserves custom server certificate validation callback
  - Server default client certificate validation accepts `SslPolicyErrors.None` and no supplied client certificate
  - Server default client certificate validation rejects certificate policy errors
  - Server custom client certificate validation callback remains user-controlled
  - Server SSL configuration copy preserves custom client certificate validation callback
- References:
  - Microsoft Learn: `RemoteCertificateValidationCallback` return value determines whether the certificate is accepted.
    https://learn.microsoft.com/dotnet/api/system.net.security.remotecertificatevalidationcallback
  - Microsoft Learn: `SslStream.AuthenticateAsClient` on .NET Framework 4.7+ can use `SslProtocols.None` to let the OS choose protocols.
    https://learn.microsoft.com/dotnet/api/system.net.security.sslstream.authenticateasclient

## 2026-06-04 - WSS loopback integration with self-signed certificate

- Branch: `codex/unity-compat-baseline`
- Command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release`
- Control command before push: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Result: Passed, 15 total, 0 failed
- Covered:
  - Runtime-generated self-signed certificate with localhost/127.0.0.1 SAN can back a secure loopback `WebSocketServer`
  - Default client server-certificate validation rejects the self-signed `wss://` endpoint
  - Custom client server-certificate validation callback is invoked for the self-signed endpoint
  - Custom validation can explicitly allow the expected self-signed certificate by thumbprint and complete secure text echo
  - Server default client-certificate validation permits one-way TLS when no client certificate is supplied
- References:
  - Microsoft Learn: `CertificateRequest.CreateSelfSigned` creates a self-signed certificate with a private key.
    https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.certificaterequest.createselfsigned
  - Microsoft Learn: `SubjectAlternativeNameBuilder` can add DNS and IP SAN entries to a test certificate.
    https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.subjectalternativenamebuilder
  - Microsoft Learn: `SslStream.AuthenticateAsServer` notes client certificates are requested, and if no certificate is provided the server still accepts the connection request.
    https://learn.microsoft.com/dotnet/api/system.net.security.sslstream.authenticateasserver

## 2026-06-04 - Async scheduling without delegate BeginInvoke

- Branch: `codex/unity-compat-baseline`
- Command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release`
- Control command before push: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Result: Passed, 16 total, 0 failed
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - `ConnectAsync` opens a loopback WebSocket connection
  - `SendAsync` invokes completion callback and round-trips text echo
  - `CloseAsync` closes the async client connection
  - Library no longer uses delegate `BeginInvoke` / `EndInvoke` scheduling

## 2026-06-04 - Async stress smoke for repeated client lifecycle

- Branch: `codex/unity-compat-baseline`
- Command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release`
- Control command before push: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Result: Passed, 17 total, 0 failed
- Covered:
  - 25 sequential `ConnectAsync` / `SendAsync` / `CloseAsync` cycles on one loopback server
  - Each async send callback completes successfully
  - Each echoed text payload matches the cycle payload
  - Server session count returns to zero after each client closes

## 2026-06-04 - Client connection timeout for silent handshake

- Branch: `codex/unity-compat-baseline`
- Command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release`
- Control command before push: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Identity check: `websocket-sharp, Version=1.0.2.32832, PublicKeyToken=5660b08a1845a91e`
- Result: Passed, 19 total, 0 failed
- Covered:
  - `ConnectionTimeout` can be configured before connecting
  - TCP connect uses the configured timeout
  - WebSocket handshake response reads use the configured timeout
  - A silent TCP peer does not keep `Connect()` waiting for the old 90 second hardcoded timeout
  - Client remains non-open after the silent handshake timeout

## 2026-06-04 - Long async lifecycle stress suite

- Branch: `codex/unity-compat-baseline`
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 19 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 1 total, 0 failed
- Stress cycles: 500 sequential `ConnectAsync` / `SendAsync` / `CloseAsync` cycles
- Stress elapsed: 00:00:02.7884107
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - Long async lifecycle repetition runs in a separate test project
  - Each echoed stress payload matches its cycle payload
  - Each async send callback completes successfully
  - Server session count returns to zero after each cycle

## 2026-06-04 - Concurrent echo stress at 50 CCU

- Branch: `codex/unity-compat-baseline`
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 19 total, 0 failed
- Targeted concurrent command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter FullyQualifiedName~ConcurrentEchoStressTests`
- Targeted concurrent result: Passed, 1 total, 0 failed
- Targeted concurrent load: 50 CCU x 100 text echo messages
- Targeted concurrent elapsed: 00:00:01.3612819
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 2 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:03.4042238
  - 50 CCU x 100 text echo messages in 00:00:01.0022753
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Development observation: an exploratory version that started all 50 clients with `ConnectAsync` at once timed out before reaching 50 open clients; this is a separate connection-storm scenario, not the active-CCU echo stress proof.
- Covered:
  - 50 simultaneously open loopback WebSocket clients
  - 5000 async text echo sends and callbacks
  - Exact received payload accounting with duplicate and missing payload detection
  - Server session count reaches 50 before message fan-out
  - Server session count returns to zero after clients close

## 2026-06-04 - Connect storm without ThreadPool starvation

- Branch: `codex/unity-compat-baseline`
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 19 total, 0 failed
- Targeted connect-storm command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter FullyQualifiedName~ConnectStormStressTests`
- Targeted connect-storm result: Passed, 1 total, 0 failed
- Targeted connect-storm load: 50 simultaneous `ConnectAsync` clients
- Targeted connect-storm elapsed: 00:00:00.3111862
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 3 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:03.0638881
  - 50 CCU x 100 text echo messages in 00:00:00.9464680
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0275477
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - `ConnectAsync` no longer occupies shared ThreadPool workers while waiting for blocking socket handshake
  - Accepted WebSocketServer requests and HttpServer WebSocket upgrades no longer depend on shared ThreadPool workers for blocking upgrade processing
  - Server session count reaches 50 after simultaneous async connect storm
  - Server session count returns to zero after storm clients close
  - Fix avoids process-wide `ThreadPool.SetMinThreads` changes
- Reference:
  - Microsoft Learn documents that `ThreadPool.SetMinThreads` is a process-wide workaround for blocked ThreadPool work and cautions that increasing it can degrade performance.
    https://learn.microsoft.com/dotnet/api/system.threading.threadpool.setminthreads

## 2026-06-04 - Server slow-handshake timeout protection

- Branch: `codex/unity-compat-baseline`
- Targeted normal command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~HandshakeTimeoutTests`
- Targeted normal result: Passed, 2 total, 0 failed
- Targeted slow-handshake command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter FullyQualifiedName~SlowHandshakeStressTests`
- Targeted slow-handshake result: Passed, 1 total, 0 failed
- Targeted slow-handshake load: 20 silent TCP clients with 250 ms server handshake timeout
- Targeted slow-handshake elapsed: 00:00:00.5119243
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 21 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 4 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:02.9671966
  - 50 CCU x 100 text echo messages in 00:00:01.0331850
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0315440
  - 20 silent TCP clients with 250 ms server timeout in 00:00:00.2734362
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - `WebSocketServer.HandshakeTimeout` can be configured before start and rejects zero or less
  - `HttpServer.HandshakeTimeout` can be configured before start and rejects zero or less
  - Raw silent TCP clients no longer wait for the old 90 second server handshake read timeout
  - A valid WebSocket client can connect, send, echo, and close while silent TCP handshakes are connected
  - Silent TCP handshakes are disconnected and no WebSocket sessions are stranded

## 2026-06-04 - Resource lifecycle stress after blocking scheduler changes

- Branch: `codex/unity-compat-baseline`
- Targeted resource command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter FullyQualifiedName~ResourceLifecycleStressTests`
- Targeted resource result: Passed, 1 total, 0 failed
- Targeted resource load:
  - Warm-up: 50 simultaneous `ConnectAsync` clients and 20 silent TCP handshakes
  - Measured: 5 rounds of 50 simultaneous `ConnectAsync` clients plus 20 silent TCP handshakes
- Targeted resource output:
  - Warm-up initial threads 27, steady-state threads 63, warm-up drift 36
  - Measured round drift from steady-state: 0, 1, 1, 1, 1
  - Final threads 63, final drift from initial 36, final steady-state drift 0, peak steady-state drift 1
  - Elapsed 00:00:05.6554616
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 21 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 5 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:03.0535726
  - 50 CCU x 100 text echo messages in 00:00:01.0853079
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0361939
  - Resource lifecycle stress final steady-state drift 0 and peak steady-state drift 1 in 00:00:05.2919700
  - 20 silent TCP clients with 250 ms server timeout in 00:00:00.2825955
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - Repeated connect-storm rounds close every client and return server sessions to zero
  - Repeated slow-handshake rounds close silent TCP clients and return server sessions to zero
  - Thread-count assertions ignore one-time CLR/process warm-up and check steady-state drift after cooldown
  - Full stress suite remains green with resource lifecycle coverage included

## 2026-06-04 - Close lifecycle and abrupt disconnect cleanup

- Branch: `codex/unity-compat-baseline`
- Targeted normal command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CloseLifecycleTests`
- Targeted normal result: Passed, 2 total, 0 failed
- Targeted close stress command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter FullyQualifiedName~CloseLifecycleStressTests`
- Targeted close stress result: Passed, 1 total, 0 failed
- Targeted close stress load: 50 concurrent repeated `CloseAsync`/`Dispose` clients plus 25 abrupt raw TCP disconnect clients
- Targeted close stress elapsed: 00:00:00.9457893
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 23 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 6 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:03.5123071
  - 50 concurrent repeated close/dispose clients plus 25 abrupt raw disconnect clients in 00:00:00.5047862
  - 50 CCU x 100 text echo messages in 00:00:01.1623874
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0513643
  - Resource lifecycle stress final steady-state drift 1 and peak steady-state drift 3 in 00:00:05.6810066
  - 20 silent TCP clients with 250 ms server timeout in 00:00:00.2833939
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - Repeated `CloseAsync` calls followed by `Dispose` emit one client close event and leave the client closed
  - Graceful repeated close/dispose returns server sessions to zero
  - Raw WebSocket clients can complete handshake and then abruptly reset TCP without close frame
  - Abrupt raw TCP disconnects return server sessions to zero
  - Full stress suite remains green with close lifecycle coverage included

## 2026-06-04 - Protocol frame validation for raw TCP clients

- Branch: `codex/unity-compat-baseline`
- Targeted protocol command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ProtocolFrameTests`
- Targeted protocol result: Passed, 8 total, 0 failed
- Targeted protocol elapsed: 00:00:00.628
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 31 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 6 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:03.3438648
  - 50 concurrent repeated close/dispose clients plus 25 abrupt raw disconnect clients in 00:00:00.4730146
  - 50 CCU x 100 text echo messages in 00:00:00.9879212
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0280684
  - Resource lifecycle stress final steady-state drift -1 and peak steady-state drift 0 in 00:00:04.7432403
  - 20 silent TCP clients with 250 ms server timeout in 00:00:00.2701398
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - Raw masked text payload boundaries 125, 126, and 66000 bytes round-trip through the server
  - Fragmented text messages can receive interleaved ping and then complete correctly
  - Unmasked client frames close the protocol-error session without delivering a message
  - Fragmented control frames close the protocol-error session without delivering a message
  - Unexpected continuation frames close the protocol-error session instead of being silently ignored
  - Invalid UTF-8 text frames close the protocol-error session instead of reaching `OnMessage`
  - Full stress suite remains green with protocol frame validation included

## 2026-06-04 - Close-frame payload and length validation

- Branch: `codex/unity-compat-baseline`
- Targeted protocol command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ProtocolFrameTests`
- Targeted protocol result: Passed, 20 total, 0 failed
- Targeted protocol elapsed: 00:00:01
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 43 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 6 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:02.8260679
  - 50 concurrent repeated close/dispose clients plus 25 abrupt raw disconnect clients in 00:00:00.5020388
  - 50 CCU x 100 text echo messages in 00:00:01.1400145
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0315033
  - Resource lifecycle stress final steady-state drift -1 and peak steady-state drift 0 in 00:00:04.8605124
  - 20 silent TCP clients with 250 ms server timeout in 00:00:00.2827046
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - One-byte close payloads return close code `1002`
  - Invalid or reserved close codes `999`, `1004`, `1005`, `1006`, `1015`, and `5000` return close code `1002`
  - Close reasons with invalid UTF-8 return close code `1007`
  - Oversized close and ping control payloads close the protocol-error session without delivering a message
  - Close frames with non-minimal extended length encoding close the protocol-error session without delivering a message
  - Full stress suite remains green with close-frame validation included

## 2026-06-04 - Unity preview versioning after project smoke test

- Branch: `codex/unity-compat-baseline`
- Unity smoke: User reported the updated DLL works in the Unity project after Editor/Standalone plugin import settings were checked.
- Versioning:
  - `AssemblyVersion` remains `1.0.2.32832` to keep Unity plugin assembly identity stable
  - `AssemblyFileVersion` set to `1.0.3.0`
  - `AssemblyInformationalVersion` set to `1.0.3.0` so file version and product version match
- Normal suite command: `dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-restore`
- Normal suite result: Passed, 43 total, 0 failed
- Stress suite command: `dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-restore --filter TestCategory=Stress`
- Stress suite result: Passed, 6 total, 0 failed
- Stress suite output:
  - 500 async lifecycle cycles in 00:00:04.2132420
  - 50 concurrent repeated close/dispose clients plus 25 abrupt raw disconnect clients in 00:00:00.4679380
  - 50 CCU x 100 text echo messages in 00:00:01.0101925
  - 50 simultaneous `ConnectAsync` clients in 00:00:00.0231285
  - Resource lifecycle stress final steady-state drift -1 and peak steady-state drift 0 in 00:00:04.6917906
  - 20 silent TCP clients with 250 ms server timeout in 00:00:00.2714969
- Additional check: `rg -n "BeginInvoke|EndInvoke" websocket-sharp tests` returned no matches
- Covered:
  - Preview version metadata records the verified Unity smoke baseline without changing strong-name assembly identity
  - File version and product version both report `1.0.3.0`
