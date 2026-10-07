# Deployment

This project is an ASP.NET Core server application. It needs a .NET host and writable persistent storage; a static-only website host or a Cloudflare JavaScript Worker cannot execute it.

## Prepare

1. Move to a supported, patched .NET release before exposing the application on the internet. The local assignment was built and verified with the installed .NET 9 SDK. Keep EF Core and Identity package major versions aligned with the target framework.
2. Run `dotnet publish -c Release -o publish`.
3. Set `ASPNETCORE_ENVIRONMENT=Production`.
4. Set `AllowedHosts` to the actual host name and provide a real HTTPS certificate/endpoint.
5. Provide the first administrator through environment variables `Seed__AdminEmail` and `Seed__AdminPassword`. Choose a unique strong password. Remove the bootstrap password from the environment after the account is created.
6. Persist `App_Data` securely: it contains the SQLite database and Data Protection keys. Restrict permissions to the app's service account. Never publish this folder as static content or check it into Git.
7. Run `dotnet AcxiomCRM.dll` from the published directory with appropriate Kestrel endpoint settings. Configure the process to restart on failure.

Use direct HTTPS Kestrel configuration or correctly configured trusted reverse-proxy forwarding. If using a proxy, explicitly configure known proxy addresses before adding forwarded-header middleware. Do not trust arbitrary forwarded headers from the internet. The app already enables HTTPS redirection, HSTS and Secure cookies outside Development.

## Production data

Production does not seed demo accounts or business records. `EnsureCreated` creates the initial empty schema. Before changing a schema that already holds real records, introduce and review EF migrations and backups; `EnsureCreated` will not upgrade an existing database.

Store the database outside ephemeral/container-only storage, back it up, and restrict who can read it. SQLite suits a small assignment or single-instance deployment. For multiple instances, use a shared supported relational database and shared protected Data Protection keys.

## Checks after deployment

Verify HTTPS and cookie flags; confirm anonymous CRM requests are rejected; log in with the bootstrapped Admin; create role-specific users; test their scopes; confirm audit entries and persisted data survive restart. Avoid running the supplied destructive test scripts on production records.

No production server, domain, certificate or public URL was provisioned as part of the local build.
