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
  - Server default client certificate validation accepts only `SslPolicyErrors.None`
  - Server default client certificate validation rejects certificate policy errors
  - Server custom client certificate validation callback remains user-controlled
  - Server SSL configuration copy preserves custom client certificate validation callback
- References:
  - Microsoft Learn: `RemoteCertificateValidationCallback` return value determines whether the certificate is accepted.
    https://learn.microsoft.com/dotnet/api/system.net.security.remotecertificatevalidationcallback
  - Microsoft Learn: `SslStream.AuthenticateAsClient` on .NET Framework 4.7+ can use `SslProtocols.None` to let the OS choose protocols.
    https://learn.microsoft.com/dotnet/api/system.net.security.sslstream.authenticateasclient
