# 🎄 C# Advent

**The site behind [csadvent.christmas](https://csadvent.christmas): two new C# posts every day from December 1st to December 25th.**

[![CI](https://img.shields.io/github/actions/workflow/status/CodingWithCalvin/CSAdvent/ci.yml?branch=main&style=for-the-badge&label=CI)](https://github.com/CodingWithCalvin/CSAdvent/actions/workflows/ci.yml)

---

## 🧱 Stack

- 🟣 **.NET 11** - Blazor Web App, static server rendering by default and interactivity only where a page needs it
- 🐘 **PostgreSQL** - every season from 2017 onward, archives included, with EF Core 11 migrations
- ✨ **Aspire** - local orchestration, telemetry, and deployment
- 🎨 **Tailwind CSS 4** - built during `dotnet build` with Tailwind's official standalone CLI, so no Node is needed
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
| `email` | MailPit: catches outgoing email, with a web inbox |

## 🗂️ Project Layout

| Path | Role |
|---|---|
| `build/Tailwind.targets` | Downloads the Tailwind CLI (checked against its SHA-256) and builds the stylesheet |
| `src/CSAdvent.AppHost` | Aspire app model: the site, PostgreSQL, migrations, and MailPit |
| `src/CSAdvent.ServiceDefaults` | Shared OpenTelemetry, health checks, resilience, and service discovery |
| `src/CSAdvent.Web` | The Blazor Web App, including the `AdventDbContext` and its migrations |
| `tests/CSAdvent.UnitTests` | Fast tests with no database or containers |
| `tests/CSAdvent.IntegrationTests` | Starts the whole AppHost against a throwaway PostgreSQL container |

## 🔐 Sign-in

- 🐙 Contributors and organizers sign in with **GitHub**. Locally that's optional: without a GitHub OAuth app, **Sign in** goes to a dev sign-in page where you can sign in as anyone (it only exists in development).
- ⚙️ To use real GitHub sign-in locally, create a GitHub OAuth app with the callback `https://localhost:7092/signin-github` and store its credentials in the AppHost's user secrets:

  ```bash
  dotnet user-secrets set "Parameters:github-client-id" "<client id>" --project src/CSAdvent.AppHost
  dotnet user-secrets set "Parameters:github-client-secret" "<client secret>" --project src/CSAdvent.AppHost
  ```

- 🛡️ Organizers are listed by GitHub user ID in `Accounts:Admins` (`src/CSAdvent.Web/appsettings.json`)

## 🗄️ Database Migrations

Migrations live in `src/CSAdvent.Web/Data/Migrations`. To add one:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/CSAdvent.Web --output-dir Data/Migrations
```

Or use **Add Migration...** on the `migrations` resource in the Aspire dashboard. Migrations run automatically when the AppHost starts. Nothing runs them at web app startup.

📚 After migrations, every new database is seeded with the 2017–2025 seasons from `src/CSAdvent.Web/Data/Archive`. Seeding only adds missing seasons, so use **Reset Database** in the dashboard to pick up changes to those files.

## 🎨 Styling

- ✏️ Style pages with Tailwind classes in the Razor components, and put theme settings (such as the `advent-*` brand colors) in `src/CSAdvent.Web/Styles/app.css`
- ⚙️ `dotnet build` generates `wwwroot/app.css` from them. That file is gitignored, so don't edit it.
- 📥 The first build downloads Tailwind's standalone CLI (about 110 MB) into `.tools/` and checks it against the release's SHA-256
- ⬆️ To upgrade Tailwind, change `TailwindVersion` and the hashes in `build/Tailwind.targets`

## 🧪 Tests

```bash
dotnet test
```

- ⚡ The unit tests need nothing else: `dotnet test --project tests/CSAdvent.UnitTests`
- 🐳 The integration tests need a running container runtime
- 🧹 They run the AppHost in the `Testing` environment: a fresh database with no data volume, no pgweb, and plain HTTP, so they never touch your local data

## 🙋 My Spots

- 📝 **Contributors add their own post's title and URL** at `/my-spots` after signing in. That's how posts get onto the calendar.
- 🚪 **Dropping out** there frees the spot straight away, without naming the author, emails the organizers, and offers the day to the backup posters

## 📝 Content

- ✍️ **Editable copy:** the About page and the Other Advents introduction are edited by organizers at `/manage/content`, in Markdown, with every version kept
- 🎄 **Other Advents:** each season has its own list of other developer advent calendars, which can be started from last year's

## 📣 Social Announcements

- 📋 **Organizers get every day's announcements at `/manage/social`**: one per post for Bluesky, Mastodon, LinkedIn, Facebook and Reddit, with the same content, each platform's formatting and limits, Copy buttons and composer links
- ✅ **Mark as posted** keeps track of what's gone out where. Links go to our entry pages, tagged with the platform

## 🛟 Backup Posters

- 🙋 **Anyone signed in can join a season's backup list** at `/backups/{year}`, kept in sign-up order
- 📨 **Spots are offered one backup at a time**, 4 hours each: straight away when an author drops out, or from 6 PM Eastern the evening before a day whose post link is still missing
- ✅ **A backup who accepts takes the spot**: immediately for a dropped spot, or at the reveal if the original author's link hasn't arrived by then
- 🧪 **Try it** without waiting for December: `POST https://localhost:7092/dev/backups/run?at=2026-12-04T23:00:00Z` (Development only)

## 🔗 Entry Pages

- 📄 **Every revealed post has its own page** at `/{year}/day/{day}/{part}`, with preview tags for Facebook, LinkedIn, Mastodon, Bluesky, Reddit, Slack, Teams and Discord
- 🖼️ **Shared links get a generated card** showing the post's title, author and day (seasons get one too), drawn with SkiaSharp and the embedded Inter font
- 📡 **RSS:** `/rss` has every revealed post from every season, newest first, and `/archive/{year}/rss` has one season
- 📈 **Read the post** goes through `/go/...`, which counts the click (people only, nothing personal) and redirects to the post. Links we publish carry `?via=` (bluesky, rss, email, calendar...), so the stats show where readers came from

## ✉️ Email

- 🎨 **Branded HTML emails** with a plain-text alternative, both rendered from one description of each email (`EmailContent`)
- 👀 **Preview every email** at <https://localhost:7092/dev/emails> while the AppHost runs (Development only). It lists when each one is sent and who gets it.
- 📬 **Nothing leaves your machine locally**: MailPit catches every email, and its inbox is linked from the dashboard
- ☁️ **In production** email goes through Azure Communication Services, from `donotreply@mail.csadvent.christmas`, with replies going to `hello@csadvent.christmas`
- ⏰ **Reminders** go out at 9 AM Eastern: post link reminders 7, 5, 3, 2 and 1 days before an author's day, and a daily "today's posts" email to every author during Advent, which has a one-click unsubscribe link
- 🧪 **Try a reminder run** without waiting for December: `POST https://localhost:7092/dev/reminders/run?at=2026-12-03T15:00:00Z` (Development only). The emails land in MailPit.

## ☁️ Deploying to Azure

Production runs on **Azure Container Apps** with **Azure Database for PostgreSQL** (Flexible Server, PostgreSQL 18, Burstable B1ms, 14-day backups) and **Application Insights**, all modeled in the AppHost.

1. 🔑 Sign in with an account that's an **Owner** of the subscription (`az login`), and make sure Docker is running
2. 📧 Set up **Azure Communication Services Email** once, in the portal. Its sending domain has to be verified in DNS, so it isn't part of `aspire deploy`:
   - Create an **Email Communication Services** resource and add the custom domain `mail.csadvent.christmas`. Add the TXT, SPF and DKIM records it shows to DNS (#17), then verify the domain.
   - Leave **user engagement tracking off**. The privacy page promises we don't track opens or clicks.
   - Under the domain's **MailFrom addresses**, keep `DoNotReply` and set its display name to **C# Advent**.
   - Create a **Communication Services** resource, connect the verified domain to it, and copy its connection string from **Keys**.
3. 🚀 Run `aspire deploy` from the repository root. It asks for the subscription, region (`eastus2`) and resource group, and for these parameters:
   - `pg-admin-username` and `pg-admin-password`: the database administrator. **The username can't be changed after the server is created**, and names like `admin`, `root` and `azure_superuser` aren't allowed. Keep the password in a password manager.
   - `github-client-id` and `github-client-secret`: the **production** GitHub OAuth app
   - `email`: the Communication Services connection string
4. 🗄️ Run the migrations job. `aspire deploy` doesn't start it, so do this after **every** deploy:

   ```bash
   az containerapp job start -n migrations -g <resource group>
   az containerapp job execution list -n migrations -g <resource group> -o table
   ```

   On a new database it creates the schema and seeds the 2017–2025 history. After that it only applies new migrations.
5. 🌐 Find the site's address: `az containerapp show -n web -g <resource group> --query properties.configuration.ingress.fqdn -o tsv`

To skip the prompts, set `Azure__SubscriptionId`, `Azure__Location`, `Azure__ResourceGroup` and the `Parameters__*` values as environment variables. `aspire publish` writes the Bicep without deploying, and needs no Azure account.

### 🌐 Moving csadvent.christmas to the new site

Azure only accepts a custom domain once its DNS points at the app, and only issues the free managed certificate after that. So the switch goes in this order, and the site has no HTTPS on csadvent.christmas for a few minutes between steps 3 and 5. `<rg>` is the resource group, `<env>` the Container Apps environment and `web` the app.

1. 🗂️ **Prepare the DNS zone** (an Azure DNS zone `csadvent.christmas` in the same resource group) with:
   - `A @` set to the environment's IP: `az containerapp env show -n <env> -g <rg> --query properties.staticIp -o tsv`
   - `TXT asuid` and `TXT asuid.www` set to the app's verification ID: `az containerapp show -n web -g <rg> --query properties.customDomainVerificationId -o tsv`
   - `CNAME www` set to the app's address: `az containerapp show -n web -g <rg> --query properties.configuration.ingress.fqdn -o tsv`
   - The email records from Azure Communication Services (SPF, DKIM, DMARC) and the MX records for hello@ forwarding
2. 🔀 **Switch the nameservers** at Hover to the zone's four Azure nameservers (`az network dns zone show -n csadvent.christmas -g <rg> --query nameServers`). This takes the old site off the domain. Wait until `nslookup -type=NS csadvent.christmas` shows them.
3. 🏷️ **Add the domains** to `CustomDomains` in `src/CSAdvent.AppHost/appsettings.json`, without certificates yet, and deploy. Both domains then work over HTTP only:

   ```json
   "CustomDomains": [
     { "Name": "csadvent.christmas" },
     { "Name": "www.csadvent.christmas" }
   ]
   ```
4. 🔐 **Create the managed certificates**, then wait until `az containerapp env certificate list -n <env> -g <rg> -o table` shows them as Succeeded:

   ```bash
   az containerapp env certificate create -n <env> -g <rg> --hostname csadvent.christmas --validation-method HTTP --certificate-name csadvent-christmas
   az containerapp env certificate create -n <env> -g <rg> --hostname www.csadvent.christmas --validation-method CNAME --certificate-name www-csadvent-christmas
   ```
5. ✅ **Add the certificate names** (`"Certificate": "csadvent-christmas"` and `"Certificate": "www-csadvent-christmas"`) and deploy again. Both domains are now on HTTPS, and www redirects to csadvent.christmas.

Keep `CustomDomains` in the AppHost, not set by hand in the portal: every deploy replaces whatever isn't in the model.

## 🔏 Privacy

- 📜 **[Privacy](https://csadvent.christmas/privacy) and [terms](https://csadvent.christmas/terms)** are pages in the site, with the organizers' address (`hello@csadvent.christmas`) from `Site:ContactEmail` in `appsettings.json`. The app won't start without it.
- 🗑️ **Contact details are deleted automatically** on February 1 after each season: spot emails, claim requests, the standby list, and contributor emails that haven't been verified again since. The calendar itself stays.
- ✏️ **Collecting something new?** Update the privacy page, and its date, in the same pull request.

## 🤝 Contributing

1. Create a branch from an up-to-date `main`, named `type/scope/short-description`
2. Use [conventional commits](https://www.conventionalcommits.org/) for commits and pull request titles
3. Open a pull request. CI builds and runs every test.
