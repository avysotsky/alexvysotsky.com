# alexvysotsky.com / VAN

Sanitized source snapshot of the software behind `alexvysotsky.com`.

## Included

- ASP.NET Core backend and exchange integrations (`backend-dotnet/`)
- Python APIs and utility services (`backend-python/`)
- current deployable frontend assets (`frontend/dist/`)
- operational and data-processing source scripts (`scripts/`, `runtime-tools/`)
- regression tests (`tests/`)

## Intentionally excluded

- databases, dumps, market/trading/account data and generated reports
- API credentials, passwords, tokens, cookies and private keys
- user logins, account numbers and production configuration
- logs, backups, rollback copies, virtual environments and build output
- TLS material, SSH material and historical Git metadata

This repository is a clean snapshot. Production configuration must be supplied
outside Git and through the deployment environment.

## Development

The .NET service entry project is:

```text
backend-dotnet/VANWebService.csproj
```

Python services are stored under `backend-python/`. The frontend currently
contains production-ready static assets because part of the portal was evolved
directly as deployable HTML/JavaScript.

Run `scripts/verify-public-snapshot.sh` before every push.
