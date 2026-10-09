# TaskBoard

A task API built with ASP.NET Core, Entity Framework Core, and PostgreSQL.

## Prerequisites

- .NET 10 SDK
- Docker with Docker Compose
- PowerShell for the commands below

Docker must be running for the local database and integration tests.

## Local setup

Run these commands from the repository root.

### 1. Restore tools and packages

```powershell
dotnet tool restore
dotnet restore .\TaskBoard.slnx --locked-mode
```

### 2. Configure the local database connection

Replace the placeholder with a private local-development password.
Use an alphanumeric password for this example so it can be embedded
directly in the connection string.

If the database volume already exists, use its original password.

```powershell
$env:TASKBOARD_DB_PASSWORD = 'REPLACE_WITH_YOUR_LOCAL_PASSWORD'

$env:ConnectionStrings__Tasks = "Host=127.0.0.1;Port=54320;Database=taskboard;Username=taskboard_dev;Password=$env:TASKBOARD_DB_PASSWORD"

$env:DOTNET_ENVIRONMENT = 'Development'
```

These variables apply to the current terminal session and its child
processes. Set them again when opening a new terminal.

Do not commit real passwords or connection strings.

`TASKBOARD_DB_PASSWORD` configures PostgreSQL through Compose.
`ConnectionStrings__Tasks` supplies the API's `ConnectionStrings:Tasks`
configuration value. The EF Core CLI commands in this document read the
same value.

### 3. Start PostgreSQL

```powershell
docker compose up --wait postgres
```

Compose runs PostgreSQL only. The API runs on the host.

The database is exposed at `127.0.0.1:54320` and stores its data in
a named Docker volume.

### 4. Apply existing migrations

```powershell
dotnet ef database update `
    --project .\src\TaskBoard.Api\TaskBoard.Api.csproj `
    --startup-project .\src\TaskBoard.Api\TaskBoard.Api.csproj
```

The API does not automatically apply migrations at startup.

### 5. Start the API

Run this in the same terminal where you configured the environment:

```powershell
dotnet run `
    --project .\src\TaskBoard.Api\TaskBoard.Api.csproj `
    --no-launch-profile `
    -- --urls http://127.0.0.1:5080
```

Leave this terminal running.

### 6. Check health

In a second terminal:

```powershell
Invoke-WebRequest http://127.0.0.1:5080/health/live |
    Select-Object StatusCode, Content

Invoke-WebRequest http://127.0.0.1:5080/health/ready |
    Select-Object StatusCode, Content
```

With PostgreSQL available, both endpoints should return HTTP 200
with `Healthy`.

| Endpoint | Purpose |
|---|---|
| `/health/live` | Application liveness; does not check PostgreSQL |
| `/health/ready` | PostgreSQL connectivity; returns 503 when unhealthy |

Readiness does not verify that migrations have been applied or that
the task schema exists.

## Required configuration

`ConnectionStrings:Tasks` is required. The API validates it at startup and
refuses to boot without it, so a misconfigured deployment fails immediately
instead of on the first request that touches the database.

Supply it either in configuration:

```json
{
  "ConnectionStrings": {
    "Tasks": "Host=127.0.0.1;Port=54320;Database=taskboard;Username=taskboard_dev;Password=..."
  }
}
```

or with the `ConnectionStrings__Tasks` environment variable, as shown in
the local setup above.

A value is accepted when Npgsql can parse it and it names both a host (or a
Unix socket path) and a database. Both requirements are checked because
Npgsql would otherwise fall back to the OS user and the local socket, which
connects to the wrong database rather than failing.

Failure messages name the configuration key and the environment-variable
form. They never include the connection string itself, including when the
value is present but malformed, because a connection string carries a
password.

### Misconfigured versus unavailable

These are separate failures and the API keeps them separate:

| Condition | Behaviour |
|---|---|
| Missing or unusable connection string | Process exits during startup |
| Correct connection string, database down | Process starts; `/health/ready` returns 503, `/health/live` returns 200 |

Startup validation only inspects the configuration text. It never opens a
connection, so a database that is merely unreachable cannot crash the
process. Use `/health/live` as the liveness signal and `/health/ready` as
the readiness signal.

## Error contract

Every error the API returns is `application/problem+json` (RFC 9457), so a
client can read one media type and one set of members regardless of which
layer rejected the request.

| Status | When | Body |
|---|---|---|
| 400 | A field failed validation, or the request envelope could not be read | Problem Details with an `errors` map |
| 404 | The task does not exist, or the path matches no endpoint | Problem Details |
| 405 | The path exists but not for that HTTP method | Problem Details |
| 409 | `version` does not match the stored task | Problem Details |
| 415 | `Content-Type` is not `application/json` | Problem Details |
| 500 | An unhandled exception | Problem Details, no diagnostic detail |

Successful responses are `application/json` and are unaffected by any of
this.

An error is `application/problem+json` whatever the client's `Accept` header
says. Success responses still negotiate normally; only errors are exempt,
because an error body in a format the client did not expect is one it cannot
act on.

### Validation errors

All 400 responses carry an `errors` map from a field name to a list of
messages, including the title a handler-produced problem uses:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "title": ["Must contain 1–200 characters after trimming."] },
  "traceId": "00-ae55ff7ae22add5e747f3a1bd39a46dc-a3990d1c79e23adf-00"
}
```

The key tells you how the request was rejected:

- A **field name** such as `title`, `version`, `pageSize`, or `cursor` means
  that parameter was rejected. Other values may still need correction.
- The key **`request`** means the body could not be deserialized at all:
  malformed JSON, an empty body, a JSON array instead of an object, or a value
  of the wrong type. No field name is given because the framework does not
  report which one it failed on.

A parameter in the query string is keyed by its own name the same way one in
the body is, so `?pageSize=abc` yields `errors.pageSize` rather than
`errors.request`. The distinction is which value was at fault: a named field
can be corrected on its own, while `errors.request` means the body is wrong
somewhere and the message is all you get.

Every problem body carries a `traceId`, including those produced for a request
the framework could not bind.

### Errors that carry no detail

404 and 500 responses describe the failure without reproducing it, and carry no
`errors` map — there is no field to correct.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "traceId": "00-f95c50a9dd1f65f7e0bbd7c428be5217-03c68ccedff4c05d-00"
}
```

A 404 does not distinguish a missing task from an unknown path. If you need to
tell them apart, check the path rather than the body.

A 500 never includes an exception message, stack trace, handler path,
connection string, or database error text, in any environment including
`Development`; that detail goes to the log. This is deliberate: an unhandled
exception here is usually a database failure whose message can carry a
connection string.

Read logs to diagnose a 500. Correlate using the `traceId` in the response
body.

### Health endpoints

`/health/live` and `/health/ready` do not return Problem Details. They
return `Healthy` or `Unhealthy` as plain text, with `/health/ready` returning
503 when unhealthy.

## Build and test

```powershell
dotnet restore .\TaskBoard.slnx --locked-mode

dotnet build .\TaskBoard.slnx `
    --configuration Release `
    --no-restore

dotnet test .\TaskBoard.slnx `
    --configuration Release `
    --no-build `
    --logger "console;verbosity=normal"
```

Integration tests create their own PostgreSQL containers. They do not
use the database started by Compose. Docker must be running.

## Intentional dependency changes

Package lock files are committed. Normal verification uses locked restore.

When intentionally changing a package reference:

1. Edit the package version in the relevant project file.
2. Regenerate lock files:

   ```powershell
   dotnet restore .\TaskBoard.slnx --force-evaluate
   ```

3. Review the project and lock-file changes.
4. Run locked restore, build, tests, and the model-drift check.
5. Commit the package-reference changes and affected lock files together.

Do not regenerate lock files merely to bypass a locked-restore failure.

NuGet provides `--force-evaluate` to reevaluate dependencies when
deliberately updating lock files.

## Check for model changes without a migration

```powershell
dotnet ef migrations has-pending-model-changes `
    --project .\src\TaskBoard.Api\TaskBoard.Api.csproj `
    --startup-project .\src\TaskBoard.Api\TaskBoard.Api.csproj `
    --configuration Release `
    --no-build
```

Run the Release build first.

This checks the EF model against the migration snapshot, not whether
a particular database has all migrations applied.

## Stop the local database

Stop the API with Ctrl+C, then run:

```powershell
docker compose down
```

The database volume is retained.

Changing `TASKBOARD_DB_PASSWORD` does not change the password in an
already-initialized database. Reuse the original password or change
the database credentials explicitly.

If PostgreSQL is running but the API reports `Unhealthy` on
`/health/ready` after a password change, PostgreSQL logs the cause:

```powershell
docker compose logs postgres
```

## Destructive local database reset

**Warning: this deletes the Compose database volume and all local task data.**

Only run this when you intentionally want a fresh local database:

```powershell
docker compose down --volumes
docker compose up --wait postgres
```

Then apply migrations again using the command above.

## Continuous integration

The GitHub Actions workflow:

- Restores local tools and NuGet packages.
- Builds the solution in Release mode.
- Runs tests.
- Checks for model changes without a migration.
- Uploads available test-result files.

This repository's Compose configuration is for local development.
It is not an API deployment configuration.
