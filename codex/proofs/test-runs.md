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
