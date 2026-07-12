# SecureAndProxyClient

This SDK-style `net472` console project demonstrates client-side settings for
`websocket-sharp` without opening a connection by default. Run it with no
arguments to print usage and exit.

The example covers:

- `SslConfiguration.ServerCertificateValidationCallback` for `wss://` clients.
  The default accepts only platform-valid certificates. `--allow-local-dev-cert`
  is an explicit exception for localhost or loopback development endpoints and
  only allows certificate chain errors. `--trusted-thumbprint` pins a specific
  server certificate thumbprint when a development endpoint has a non-standard
  certificate name.
- `SetProxy` with optional proxy credentials.
- `Compression` via `CompressionMethod.Deflate`, with `--no-compression` to
  disable the request.
- `ConnectionTimeout`.
- Bounded opt-in redirects with `EnableRedirection` and `MaxRedirections`
  (`0..100`, default `5`). Cross-origin redirects omit HTTP credentials,
  cookies, user headers, and TLS client certificates, including reconnects.
  Secure `wss://` to insecure `ws://` downgrade remains blocked unless
  `--allow-insecure-redirect` is supplied explicitly.
- `Origin` and `SetUserHeader`.

## Build

```powershell
dotnet build .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj
```

## Run

```powershell
dotnet run --project .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -- --help
dotnet run --project .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -- wss://localhost:5963/Echo --allow-local-dev-cert
dotnet run --project .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -- wss://localhost:5963/Echo --trusted-thumbprint <sha1>
dotnet run --project .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -- wss://example.com/Echo --proxy http://localhost:3128
dotnet run --project .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -- ws://localhost:4649/Chat --origin http://localhost:4649 --header RequestForID=ID
```
