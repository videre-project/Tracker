# Tracker server tests

Tests are grouped by the boundary they exercise:

- `Controllers/` — HTTP contracts, controller actions, and stream endpoints.
- `Database/` — EF Core persistence and database codecs.
- `Services/` — client state, collection/trade writers, and event ingestion.
- `Fixtures/` — sanitized Wine/Docker reference-data checks.
- `Infrastructure/` — in-process host, temporary database setup, and fakes.

When adding a controller test, start with `Controllers/` and `ControllerTestBase` for an HTTP test-host test. Keep persistence-only assertions in `Database/` and service-only assertions in `Services/`. Fixture JSON files are reference data, not tests by themselves.
