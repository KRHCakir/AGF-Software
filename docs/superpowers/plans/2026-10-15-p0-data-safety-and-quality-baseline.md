# P0 Data Safety and Quality Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Make the existing prototype safe to start, reproducible to build, and protected by an initial automated quality gate without changing AGF business behaviour.

**Architecture:** Extract demo-data startup behaviour from Program.cs into a development-only, idempotent seeder. Keep EF Core migrations as the only schema startup operation and fail early when no database connection string is configured. Move all local database values to ignored environment configuration and add a GitHub Actions workflow that validates backend tests/build and frontend lint/build.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core 9 with Npgsql, PostgreSQL 15, Next.js 15, React 19, TypeScript, ESLint 9, GitHub Actions.

**Spec:** docs/superpowers/specs/2026-10-15-agf-desktop-mvp-design.md

## Global Constraints

- Never call EnsureDeleted, EnsureDeletedAsync, EnsureCreated or EnsureCreatedAsync from application startup.
- Production/demo customer data must survive every application restart.
- No credential, API key, production URL or customer data may be committed.
- Do not introduce business features, change stock calculations or alter invoice behaviour in this plan.
- Use migrations for EF Core schema changes.
- Keep the frontend on Next.js 15 and use ESLint CLI; do not use next lint.
- Every changed behaviour needs an automated test or an explicit manual verification command.
- Every task is implemented on its own feature branch and reviewed as its own pull request.

## Review Focus

- Restarting the API against an existing database must retain all pre-existing rows; this is pinned in Task 2 by asserting the startup initializer does not delete or reseed a non-empty store.
- A missing ConnectionStrings:DefaultConnection value must fail before the application registers Npgsql; this is pinned in Task 1 with a missing-value unit test.
- Demo data must be impossible in a default or production startup; this is pinned in Task 2 by tests for disabled and non-development seeding.
- A developer must be able to start local Postgres without copying a committed password; this is pinned in Task 3 by environment-template documentation and compose interpolation verification.
- The quality workflow must use the committed npm lockfile and the actual .NET target version; this is pinned in Task 4 by a pull-request workflow run.

---

## File structure

| Path | Responsibility |
| --- | --- |
| AgfAdmin/Configuration/DatabaseConfiguration.cs | Single required-connection-string validation boundary |
| AgfAdmin/Services/DevelopmentDataSeeder.cs | Idempotent demo data creation; never deletes data |
| AgfAdmin/Services/IDevelopmentDataSeeder.cs | Small seeder interface used by startup |
| AgfAdmin/Program.cs | Composition root: migration, development-only optional seed, configuration validation |
| AgfAdmin.Tests/ | Focused xUnit tests for configuration and seeding |
| AgfAdmin/appsettings.json | Non-secret shared settings only |
| AgfAdmin/appsettings.Development.json.example | Copyable local configuration template without real credentials |
| .env.example | Copyable Docker Compose environment template without a real password |
| docker-compose.yml | Reads local Postgres settings from environment variables |
| docs/development-setup.md | Reproducible local setup and secret-handling instructions |
| agf-frontend/package.json | ESLint CLI script compatible with the checked-in flat config |
| .github/workflows/quality.yml | Pull-request build, test, lint and frontend build gate |
| .vscode/launch.json | Correct .NET 9 debugger target |

## Task 1: Add configuration validation and test foundation

**Files:**
- Create: AgfAdmin/Configuration/DatabaseConfiguration.cs
- Create: AgfAdmin.Tests/AgfAdmin.Tests.csproj
- Create: AgfAdmin.Tests/Configuration/DatabaseConfigurationTests.cs
- Modify: Software-AGF.sln
- Modify: AgfAdmin/Program.cs
- Modify: AgfAdmin/AgfAdmin.csproj

**Interfaces:**
- Produces: DatabaseConfiguration.GetRequiredConnectionString(IConfiguration configuration) returns a non-empty connection string or throws InvalidOperationException with the exact message "Connection string 'DefaultConnection' is required."
- Consumes: IConfiguration and the standard ConnectionStrings section.
- Produces: an xUnit test project targeting net9.0 and referencing AgfAdmin.

- [ ] **Step 1: Create the xUnit project and add it to the solution**

Add Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio and Microsoft.EntityFrameworkCore.InMemory at versions compatible with EF Core 9.0.7. Reference AgfAdmin.csproj and add the project to Software-AGF.sln.

- [ ] **Step 2: Write failing configuration tests**

Create DatabaseConfigurationTests with:
- Missing_DefaultConnection_Throws_Clear_Configuration_Error: an empty in-memory configuration throws InvalidOperationException with the exact required message.
- DefaultConnection_Returns_Configured_Value: a configuration containing ConnectionStrings:DefaultConnection returns that same string.

- [ ] **Step 3: Run the configuration tests to verify they fail**

Run: dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~DatabaseConfigurationTests
Expected: FAIL because DatabaseConfiguration does not exist.

- [ ] **Step 4: Implement DatabaseConfiguration.GetRequiredConnectionString**

Read only configuration.GetConnectionString("DefaultConnection"). Reject null, empty and whitespace values with the exact exception text from Step 2. Replace Program.cs direct GetConnectionString access with this method before options.UseNpgsql.

- [ ] **Step 5: Re-run the configuration tests**

Run: dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~DatabaseConfigurationTests
Expected: PASS.

- [ ] **Step 6: Commit**

Commit message: test: add required database configuration guard

## Task 2: Replace destructive startup with safe development seeding

**Files:**
- Create: AgfAdmin/Services/IDevelopmentDataSeeder.cs
- Create: AgfAdmin/Services/DevelopmentDataSeeder.cs
- Create: AgfAdmin.Tests/Services/DevelopmentDataSeederTests.cs
- Modify: AgfAdmin/Program.cs

**Interfaces:**
- Produces: IDevelopmentDataSeeder.SeedAsync(AppDbContext db, CancellationToken cancellationToken = default).
- Produces: DevelopmentDataSeeder, which adds the existing demo records only when explicitly invoked and only if the corresponding table is empty.
- Consumes: AppDbContext and the existing Product, ProductVariant, Vendor, IncomingInvoice, Customer and Shipment entities.
- Program.cs consumes IDevelopmentDataSeeder and configuration key Development:SeedDemoData.

- [ ] **Step 1: Write failing seeder tests**

Using an EF Core InMemory AppDbContext:
- SeedAsync_Seeds_An_Empty_Development_Database creates the current demonstration product, variant, vendor, incoming invoice, customer and shipment data.
- SeedAsync_Is_Idempotent preserves pre-existing records and does not duplicate the seed records after a second call.
- SeedAsync_Does_Not_Delete_Existing_Data inserts a sentinel Customer before seeding and asserts it remains after seeding.

- [ ] **Step 2: Run the seeder tests to verify they fail**

Run: dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~DevelopmentDataSeederTests
Expected: FAIL because IDevelopmentDataSeeder and DevelopmentDataSeeder do not exist.

- [ ] **Step 3: Extract the existing literal demo seed blocks into DevelopmentDataSeeder**

Move only the current demo objects from Program.cs into DevelopmentDataSeeder. Keep the existing empty-table guards. Do not add any call to EnsureDeleted, database deletion, raw SQL reset or migration creation.

- [ ] **Step 4: Wire safe startup in Program.cs**

Register IDevelopmentDataSeeder as scoped. In the startup service scope:
1. await db.Database.MigrateAsync();
2. invoke SeedAsync only when app.Environment.IsDevelopment() is true and Development:SeedDemoData evaluates true;
3. otherwise do not insert demo data.

Remove the complete inline seeding block and the EnsureDeleted call. The default value for Development:SeedDemoData is false when the key is absent.

- [ ] **Step 5: Run test and source guards**

Run:
- dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~DevelopmentDataSeederTests
- rg "EnsureDeleted|EnsureCreated" AgfAdmin -g '*.cs'

Expected:
- all seeder tests PASS;
- ripgrep produces no startup call; any historical migration metadata must be reviewed rather than deleted.

- [ ] **Step 6: Commit**

Commit message: fix: preserve data on application startup

## Task 3: Externalise local configuration and document safe setup

**Files:**
- Modify: AgfAdmin/appsettings.json
- Create: AgfAdmin/appsettings.Development.json.example
- Modify: .gitignore
- Modify: docker-compose.yml
- Create: .env.example
- Create: docs/development-setup.md

**Interfaces:**
- Consumes: environment variable ConnectionStrings__DefaultConnection for the API and POSTGRES_USER, POSTGRES_PASSWORD and POSTGRES_DB for local Compose.
- Produces: a documented local setup that never requires a committed secret.
- Produces: an ignored AgfAdmin/appsettings.Development.json copied locally from its example.

- [ ] **Step 1: Write/update configuration regression tests**

Extend DatabaseConfigurationTests with DefaultConnection_From_EnvironmentStyle_Key_Is_Accepted. Build configuration using the key ConnectionStrings:DefaultConnection and assert the value is returned.

- [ ] **Step 2: Run the configuration tests**

Run: dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~DatabaseConfigurationTests
Expected: PASS before configuration file cleanup; the test protects the expected configuration contract.

- [ ] **Step 3: Remove committed connection values and add templates**

Remove the ConnectionStrings value containing a password from AgfAdmin/appsettings.json. Add appsettings.Development.json.example showing the ConnectionStrings:DefaultConnection key with a non-working placeholder. Add .env.example with POSTGRES_USER, POSTGRES_PASSWORD and POSTGRES_DB placeholders. Add explicit .gitignore entries for AgfAdmin/appsettings.Development.json and root .env while retaining both example files.

- [ ] **Step 4: Make Docker Compose consume local environment values**

Replace literal Postgres user/password/database values in docker-compose.yml with required environment interpolation. Keep PostgreSQL major version 15 and do not add a production database configuration to Compose.

- [ ] **Step 5: Document setup and credential rotation**

Write docs/development-setup.md covering:
1. copy .env.example to .env and replace placeholders;
2. copy appsettings.Development.json.example to appsettings.Development.json or set ConnectionStrings__DefaultConnection;
3. start PostgreSQL with docker compose up;
4. run backend/frontend commands;
5. rotate the already committed database password before any non-local deployment; removing it from the current tree does not erase Git history.

- [ ] **Step 6: Verify tracked files contain no known credential literal**

Run:
- git grep -n "Password=admin"
- git grep -n "POSTGRES_PASSWORD: admin"

Expected: no matches in tracked working-tree files.

- [ ] **Step 7: Commit**

Commit message: chore: externalize local database configuration

## Task 4: Restore a trustworthy local and CI quality gate

**Files:**
- Modify: agf-frontend/package.json
- Create: .github/workflows/quality.yml
- Modify: .vscode/launch.json
- Modify: AgfAdmin/agf-admin.http

**Interfaces:**
- Produces: npm run lint executing eslint . against the existing eslint.config.mjs.
- Produces: a GitHub Actions quality workflow that runs on pull_request and workflow_dispatch.
- Consumes: Node 20, the committed agf-frontend/package-lock.json and .NET 9.

- [ ] **Step 1: Write a failing lint-command check**

Run: npm run lint from agf-frontend.
Expected: the current next lint command fails or is deprecated under the installed Next.js 15.4.6; capture the output in the pull request description, not in source code.

- [ ] **Step 2: Change the lint script**

Set package.json scripts.lint exactly to eslint .. Do not change eslint.config.mjs rules in this task.

- [ ] **Step 3: Verify frontend static quality**

Run from agf-frontend:
- npm ci
- npm run lint
- npm run build

Expected: all commands PASS. If lint or build exposes existing application defects, stop this task and create a separate defect plan/PR; do not silence rules or change business behaviour merely to turn CI green.

- [ ] **Step 4: Add CI workflow**

Create quality.yml with separate backend and frontend jobs:
- backend: actions/checkout, actions/setup-dotnet with 9.0.x, dotnet restore Software-AGF.sln, dotnet build Software-AGF.sln --no-restore --configuration Release, dotnet test Software-AGF.sln --no-build --configuration Release;
- frontend: actions/checkout, actions/setup-node with Node 20 and npm cache path agf-frontend/package-lock.json, npm ci, npm run lint and npm run build with working-directory agf-frontend.

- [ ] **Step 5: Repair stale developer probes**

Update .vscode/launch.json from net8.0 to net9.0. Replace the default weatherforecast request in AgfAdmin/agf-admin.http with a documented health/manual smoke request that matches an existing endpoint; do not add a new endpoint solely for this file.

- [ ] **Step 6: Verify the complete P0 quality baseline**

Run:
- dotnet restore Software-AGF.sln
- dotnet build Software-AGF.sln --no-restore --configuration Release
- dotnet test Software-AGF.sln --no-build --configuration Release
- cd agf-frontend && npm ci && npm run lint && npm run build

Expected: every command exits 0. Confirm the GitHub Actions workflow passes on the pull request before merge.

- [ ] **Step 7: Commit**

Commit message: ci: add backend and frontend quality gate

## Plan self-review

- **Spec coverage:** This plan implements the P0 data-preservation, secret-handling, build-verification, development setup and quality-gate requirements. Authentication/roles, broken business endpoints, stock ledger and AI are deliberately separate plans because they are independent subsystems.
- **Step scan:** Each task has a defined interface, test/failure expectation, implementation boundary, verification and commit.
- **Type consistency:** DatabaseConfiguration is consumed only by Program.cs; IDevelopmentDataSeeder is implemented by DevelopmentDataSeeder and consumed by Program.cs; both tests reference their production interfaces.
- **Review focus coverage:** The five listed risks are each pinned to Task 1, 2, 3 or 4.
- **Proportion:** The plan defines boundaries and verification without transcribing production code.
