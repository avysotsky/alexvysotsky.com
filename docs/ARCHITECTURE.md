# Architecture

The repository is split into five main areas:

- `backend-dotnet/` — ASP.NET Core APIs, account abstractions, exchange clients,
  collectors and trading services.
- `backend-python/` — Python APIs and lightweight frontend/data services.
- `frontend/dist/` — static public and authenticated portal assets.
- `runtime-tools/` — operational collectors and transformation utilities.
- `tests/` — browser/static regression tests.

Production databases, runtime configuration and external service credentials are
deliberately not part of this repository.
