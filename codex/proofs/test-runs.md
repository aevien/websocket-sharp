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
