# 🎄 C# Advent

**The site behind [csadvent.christmas](https://csadvent.christmas): two new C# posts every day from December 1st to December 25th.**

[![CI](https://img.shields.io/github/actions/workflow/status/CodingWithCalvin/CSAdvent/ci.yml?branch=main&style=for-the-badge&label=CI)](https://github.com/CodingWithCalvin/CSAdvent/actions/workflows/ci.yml)

---

## ✨ What It Does

- **The calendar.** Each day's posts are revealed at midnight Eastern. Every season since 2017 is in the archive, with author pages, search, RSS and a look back at each year.
- **Sign-ups.** Authors sign in with GitHub to request a spot, and organizers approve requests. Then each author adds their own post on My spots.
- **Keeping the calendar full.** Reminders and a daily email go to authors. A backup list steps in when a post is missing or an author drops out.
- **The blurb check.** Each post must open by linking back to the calendar, and Claude Haiku (through Anthropic's API) checks that it does.
- **Social announcements.** Posts go out automatically on Bluesky, Mastodon, Facebook and X, and are ready to copy for LinkedIn and Reddit.
- **An email list,** for announcements and each day's posts. Everything the site sends goes through one queue that keeps it under Azure's email limits.
- **A hidden test season,** for rehearsing December on the live site at any time of year.

## 🧱 Stack

- **.NET 11:** a Blazor Web App, rendered on the server with plain HTML forms and no Blazor script in the browser
- **PostgreSQL:** every season, archives included, with EF Core 11 migrations
- **Aspire:** local orchestration, telemetry, and deployment to Azure Container Apps
- **Tailwind CSS 4:** built during `dotnet build` with Tailwind's standalone CLI, so no Node is needed
- **MailPit:** catches every email sent in development, so nothing reaches real people

## 🚀 Getting Started

You need:

- the **.NET 11 SDK** pinned in [`global.json`](global.json);
- a container runtime: Docker Desktop, Rancher Desktop, or Podman;
- optionally, the **Aspire CLI**: `dotnet tool install --global Aspire.Cli`.

1. Clone the repository.
2. Trust the HTTPS development certificate (once per machine): `aspire certs trust`.
3. Run the AppHost from the repository root with `aspire run`, or with `dotnet run --project src/CSAdvent.AppHost`, or from your IDE.
4. Open the dashboard link printed in the terminal.

If something doesn't start, `aspire doctor` checks your SDK, container runtime, and certificates.

| Resource | What it is |
|---|---|
| `web` | The site, at <https://localhost:7092> |
| `postgres` / `csadvent` | PostgreSQL and the site's database, in a Docker volume that survives restarts. It's seeded with the 2017–2025 seasons. |
| `migrations` | Applies EF Core migrations before `web` starts |
| `pgweb` | A browser UI for the database |
| `email` | MailPit's inbox |

**Sign-in:** locally, **Sign in** goes to a development page where you can sign in as anyone. To use real GitHub sign-in instead:

1. Create a GitHub OAuth app with the callback `https://localhost:7092/signin-github`.
2. Store its client ID and secret as the AppHost's user secrets `Parameters:github-client-id` and `Parameters:github-client-secret`.

**Migrations:** add one with `dotnet tool restore`, then `dotnet ef migrations add <Name> --project src/CSAdvent.Web --output-dir Data/Migrations`. Or use **Add Migration...** on the `migrations` resource in the dashboard.

## 🧪 Tests

```bash
dotnet test                                       # everything; the integration tests need a container runtime
dotnet test --project tests/CSAdvent.UnitTests    # unit tests only
```

The integration tests start the whole AppHost against a throwaway database, so they never touch your local data.

## 🧰 Trying Things Locally

Everything here works in Development only. Emails land in MailPit.

| Address | What it does |
|---|---|
| `/dev/emails` | Previews every email with sample data, with when it's sent and who gets it |
| `POST /dev/reminders/run?at=2026-12-03T15:00:00Z` | Sends the reminders and daily emails due at that time |
| `POST /dev/backups/run?at=…&year=…` | Asks backup posters about spots that need them |
| `POST /dev/blurbs/run?at=…&year=…` | Runs the blurb check, against built-in sample posts and a stand-in for Claude |
| `POST /dev/social/run?at=…` | Runs automatic social posting in dry-run mode, which only logs |
| `POST /dev/announcements/run?at=…&year=…` | Queues announcements and invitations that are due |
| `POST /dev/links/run?at=…&year=…` | Runs the dead-link check, against built-in sample pages |

## 🗂️ Project Layout

| Path | Role |
|---|---|
| `src/CSAdvent.AppHost` | Aspire app model: the site, PostgreSQL, migrations and MailPit locally, and everything in Azure |
| `src/CSAdvent.ServiceDefaults` | OpenTelemetry, health checks, resilience, and service discovery |
| `src/CSAdvent.Web` | The Blazor Web App, its `AdventDbContext` and migrations |
| `tests/CSAdvent.UnitTests` | Fast tests with no database or containers |
| `tests/CSAdvent.IntegrationTests` | Tests against the whole AppHost |
| `build/Tailwind.targets` | Downloads the Tailwind CLI, checked against its SHA-256, and builds the stylesheet |

## 📚 Documentation

- [`CLAUDE.md`](CLAUDE.md): how everything works, and the conventions to follow
- [`docs/runbook.md`](docs/runbook.md): the yearly cycle, credentials, alerts, and what to do when something breaks
- [`docs/deployment.md`](docs/deployment.md): deploying to Azure, and the one-time setup behind it

## 🤝 Contributing

1. Create a branch from an up-to-date `main`, named `type/scope/short-description`.
2. Use [conventional commits](https://www.conventionalcommits.org/) for commits and pull request titles.
3. Open a pull request. CI builds and runs every test, unless the pull request only changes `README.md`, `CLAUDE.md` or `docs/`.
