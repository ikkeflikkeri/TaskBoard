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