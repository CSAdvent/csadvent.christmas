# 🎄 C# Advent

**The site behind [csadvent.christmas](https://csadvent.christmas): two new C# posts every day from December 1st to December 25th.**

[![CI](https://img.shields.io/github/actions/workflow/status/CodingWithCalvin/CSAdvent/ci.yml?branch=main&style=for-the-badge&label=CI)](https://github.com/CodingWithCalvin/CSAdvent/actions/workflows/ci.yml)

---

## 🧱 Stack

- 🟣 **.NET 11** - Blazor Web App, static server rendering by default and interactivity only where a page needs it
- 🐘 **PostgreSQL** - every season from 2017 onward, archives included, with EF Core 11 migrations
- ✨ **Aspire** - local orchestration, telemetry, and deployment
- 📬 **MailPit** - catches every email sent in development, so nothing reaches real people

## 🛠️ Prerequisites

- 📦 The **.NET 11 SDK** pinned in [`global.json`](global.json)
- 🐳 A container runtime: Docker Desktop, Rancher Desktop, or Podman
- 🚀 The **Aspire CLI** (optional, but it's the easiest way to run the app): `dotnet tool install --global Aspire.Cli`

## 🚀 Getting Started

1. Clone the repository
2. Trust the HTTPS development certificate (once per machine): `aspire certs trust`
3. Run the AppHost from the repository root:

   ```bash
   aspire run
   ```

4. Open the dashboard link printed in the terminal 🎉

`aspire doctor` checks your SDK, container runtime, and certificates if something doesn't start.

You can also run the AppHost without the Aspire CLI, with `dotnet run --project src/CSAdvent.AppHost` or from your IDE.

### 🧩 What Starts

| Resource | What it is |
|---|---|
| `web` | The Blazor site |
| `postgres` / `csadvent` | PostgreSQL and the site's database, kept in a Docker volume so your data survives restarts |
| `migrations` | Applies EF Core migrations before `web` starts |
| `pgweb` | A browser UI for the database |
| `mailpit` | Catches outgoing email, with a web inbox |

## 🗂️ Project Layout

| Path | Role |
|---|---|
| `src/CSAdvent.AppHost` | Aspire app model: the site, PostgreSQL, migrations, and MailPit |
| `src/CSAdvent.ServiceDefaults` | Shared OpenTelemetry, health checks, resilience, and service discovery |
| `src/CSAdvent.Web` | The Blazor Web App, including the `AdventDbContext` and its migrations |
| `tests/CSAdvent.UnitTests` | Fast tests with no database or containers |
| `tests/CSAdvent.IntegrationTests` | Starts the whole AppHost against a throwaway PostgreSQL container |

## 🗄️ Database Migrations

Migrations live in `src/CSAdvent.Web/Data/Migrations`. To add one:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/CSAdvent.Web --output-dir Data/Migrations
```

Or use **Add Migration...** on the `migrations` resource in the Aspire dashboard. Migrations run automatically when the AppHost starts. Nothing runs them at web app startup.

📚 After migrations, every new database is seeded with the 2017–2025 seasons from `src/CSAdvent.Web/Data/Archive`. Seeding only adds missing seasons, so use **Reset Database** in the dashboard to pick up changes to those files.

## 🧪 Tests

```bash
dotnet test
```

- ⚡ The unit tests need nothing else: `dotnet test --project tests/CSAdvent.UnitTests`
- 🐳 The integration tests need a running container runtime
- 🧹 They run the AppHost in the `Testing` environment: a fresh database with no data volume, no pgweb, and plain HTTP, so they never touch your local data

## 🤝 Contributing

1. Create a branch from an up-to-date `main`, named `type/scope/short-description`
2. Use [conventional commits](https://www.conventionalcommits.org/) for commits and pull request titles
3. Open a pull request. CI builds and runs every test.
