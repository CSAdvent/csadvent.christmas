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
- 📜 **Your C# Advent history:** returning authors see every post they've written, season by season, once an organizer has linked their archive posts to their GitHub account at `/manage/authors`. Organizers see them as returning on their requests
- 📈 **How your posts did:** each revealed post shows its visits and clicks, to its author only
- 🙋 **Older posts:** wrote for an earlier season? Find your posts on My spots, or from the post's page, and ask for them to be added to your account. Every request emails the organizers, who approve or decline it at `/manage/authors/requests`. A post already on someone else's account goes through hello@csadvent.christmas instead
- 🚚 **Moved your blog?** Authors give their older posts' new links in one go from My spots (`/my-spots/links`). An organizer checks them and approves or declines the whole request at `/manage/authors/link-changes`, and can change any author's links directly from their author page. The calendar, the archive and entry pages link through `/go`, so a new link applies to all of them at once
- 🪪 **Profiles:** contributors set the name, website and social accounts they're credited with at `/profile`, and can put them on every spot they've had. It fills in their next claim
- 🐙 **Accounts from GitHub:** the first sign-in fills in any Bluesky, Mastodon, LinkedIn, Facebook, Reddit or X account linked on the contributor's GitHub profile, and **Fill in from GitHub** on their profile does it again
- 🔗 **Author links:** an author's name links to their website or blog, or to their GitHub profile if they didn't give one. Social media profiles go in their own fields, and each entry's page links the author's website, GitHub and social accounts
- 👥 **Authors:** organizers see every author across every season at `/manage/authors`, with each season linking to the post. They can edit anyone's profile, combine names that belong to the same person, and rename or link archive authors

## 📝 Content

- ✍️ **Editable copy:** the About page and the Other Advents introduction are edited by organizers at `/manage/content`, in Markdown, with every version kept
- 🎄 **Other Advents:** each season has its own list of other developer advent calendars, which can be started from last year's
- 🖼️ **Logos** for the Other Advents are fetched from each calendar's site, redrawn as small PNGs and served from our own domain. Organizers can refresh one or upload a better one

## 📬 Announcements by Email

- ✍️ **Anyone can sign up** at `/announcements` (or on the home page between seasons) with a confirmed email, kept until they unsubscribe
- 📢 **Organizers send announcements** from `/manage/announcements`: draft, test to the organizers' inbox, then send. They go out 40 an hour, to stay under the email service's limit
- 🗓️ **"Sign-ups are open"** goes out by itself at 9 AM Eastern on the day each season's sign-ups open
- 💌 **Past authors are invited back** at the same time, once each, unless they're already on the list, have a spot or request, or unsubscribed
- 🚪 **Every email has a one-click unsubscribe**, which deletes the address

## 📣 Social Announcements

- 📆 **Sign-ups posts** at `/manage/social/signups`: "sign-ups open in 8 days" whenever you like, and "sign-ups are open", which Automatic platforms post by themselves at 9 AM Eastern on the day
- 🤖 **Automatic posting** to Mastodon, Bluesky, the Facebook Page and X: each platform is Automatic, Manual or Off on `/manage/social`, changed without a redeploy. Automatic ones post each entry at 10 AM (post 1) or 2 PM (post 2) Eastern on its day, or as soon as its link arrives (or its blurb check passes) after that, exactly once even with several replicas. Failures retry a few times, then show the platform's error with a **Retry now** button. **Send a test post** posts a labelled test, formatted like an announcement, to check how posts look. Each Automatic platform's credentials are checked every morning, and the organizers get an email if one stops working. Development and the tests use dry-run mode, which only logs, so they never reach a real account

- 📋 **Organizers get every day's announcements at `/manage/social`**: one per post for Bluesky, Mastodon, LinkedIn, Facebook, Reddit and X, with the same content, each platform's formatting and limits, Copy buttons and composer links
- ✅ **Mark as posted** keeps track of what's gone out where. Links go to our entry pages, tagged with the platform

## 🛟 Backup Posters

- 🙋 **Anyone signed in can join a season's backup list** at `/backups/{year}`, kept in sign-up order
- 📨 **Spots are offered one backup at a time**, 4 hours each: straight away when an author drops out, or from 6 PM Eastern the evening before a day whose post link is still missing
- ✅ **A backup who accepts takes the spot**: immediately for a dropped spot, or at the reveal if the original author's link hasn't arrived by then
- ✍️ **A post without the C# Advent blurb** lines up a backup from the reveal, an hour each, and one who accepts (with the blurb in their own post) takes the spot at the post's announcement, unless the author adds it first
- 🧪 **Try it** without waiting for December: `POST https://localhost:7092/dev/backups/run?at=2026-12-04T23:00:00Z` (Development only)

## ✍️ The C# Advent Blurb

- 📌 **Every post opens with the blurb** from 2026 on: a sentence saying it's part of that year's C# Advent calendar, with a link to csadvent.christmas, in the author's own words. My spots has one ready to paste, as text, Markdown or HTML
- 🔎 **Each post is checked automatically** from the day before its reveal: we fetch it ourselves (public addresses only), read the top of the article, and fail it straight away without a link back near the top
- 🤖 **Claude Haiku 5.5 checks the rest**, through Anthropic's API. Its quote of the blurb has to really be in the post, and anything it's unsure about goes to an organizer
- 📨 **A missing blurb emails the author** once, with the blurb to paste and the deadline (the post's announcement), and lines up a backup. Fixing it is noticed within 15 minutes, with no reply needed
- ⏸️ **Announcements wait for it**: automatic social posts only go out once a post passes (or an organizer lets it through), and the daily email leaves out a post without it
- 🧑‍⚖️ **Organizers decide on each entry's page**: Claude's answer, **Check now**, **Mark as passed**, **Mark as missing**, **Announce anyway** and **Use a backup**
- 🧪 **Try it**: `POST https://localhost:7092/dev/blurbs/run?at=2026-12-05T06:00:00Z` (Development only). Development and the tests use a stand-in for Claude and built-in sample posts (`https://samples.csadvent.test/with-blurb?year=2026` and the others in `SamplePosts`), so they never reach Anthropic or the internet

## 🔗 Entry Pages

- 📄 **Every revealed post has its own page** at `/{year}/day/{day}/{part}`, with preview tags for Facebook, LinkedIn, Mastodon, Bluesky, Reddit, X, Slack, Teams and Discord
- 🖼️ **Shared links get a generated card** showing the post's title, author and day (seasons get one too), drawn with SkiaSharp and the embedded Inter font
- 📡 **RSS:** `/rss` has every revealed post from every season, newest first, and `/archive/{year}/rss` has one season
- 📈 **Read the post** goes through `/go/...`, which counts the click (people only, nothing personal) and redirects to the post. Links we publish carry `?via=` (bluesky, rss, email, calendar...), so the stats show where readers came from

## 🎁 Retrospectives

- 📝 **A look back at every season** at `/archive/{year}/retrospective`, linked from the season's archive page, in the RSS feeds, and shared on social media from the organizers' page
- ✍️ **Organizers generate a draft** at `/manage/retrospectives`, written from the season's numbers, then rewrite it in Markdown, preview it and publish it. **Generate drafts** backfills every past season at once
- 📊 **By the numbers:** posts, authors, first-time and returning authors, how many seasons this year's authors have written for, posts and authors every season since 2017, and, from 2026 on, clicks per day, where they came from and the ten most-read posts. The charts are plain HTML, with a table of every value
- 🔒 **Only totals are public:** authors see their own posts' numbers on My spots, and nothing about readers is recorded

## 🧪 Testing a Season

A hidden test season lets invited testers try everything for real on the live site, at any time of year, without touching the real seasons.

- 🚦 **Create the test season** at `/manage/testing` with the date that stands in for December 1
- 👥 **Invite testers** by GitHub login. Only they and the organizers can see it; to everyone else it doesn't exist
- 🔀 **Test mode** puts the test calendar on a tester's home page. Testers get an invitation to switch, and a banner to switch back
- ⏩ **Rehearse December any time:** reveals, reminders, the daily email and backup asks follow the rehearsal. Move it to jump ahead, or send what's due straight away
- 🧹 **Reset the test season** to delete everything done in it: every spot is open again, and the requests, backups, emails, visits and social marks are gone
- 🛑 **End testing** to delete the test season itself, with its testers and everything done in it. Testers still in test mode just see the real site, and you can create a new test season for another round

## ✉️ Email

- 🎨 **Branded HTML emails** with a plain-text alternative, both rendered from one description of each email (`EmailContent`)
- 📌 **Emails about a spot, request or backup offer always go out**, because people signed up for them, so they have no unsubscribe link
- 🔕 **Every other email can unsubscribe**, with a link and a one-click `List-Unsubscribe` header. Unsubscribing stops all of C# Advent's optional emails to that address (the daily email and announcements have their own links too), and they can be turned back on from the same page or the profile. Only a fingerprint of the address is kept
- 👀 **Preview every email** at <https://localhost:7092/dev/emails> while the AppHost runs (Development only). It lists when each one is sent and who gets it.
- 📮 **Send any email now** from `/manage/emails` on the live site: organizers preview each one, and send it with sample data to themselves, the organizers' inbox or a tester, without waiting for the date that would trigger it
- 📬 **Nothing leaves your machine locally**: MailPit catches every email, and its inbox is linked from the dashboard
- ☁️ **In production** email goes through Azure Communication Services, from `donotreply@mail.csadvent.christmas`, with replies going to `hello@csadvent.christmas`
- ⏰ **Reminders** go out at 9 AM Eastern: post link reminders 7, 5, 3, 2 and 1 days before an author's day, and a daily "today's posts" email to every author during Advent, which has a one-click unsubscribe link
- 🚨 **A last call on the day:** an author whose link still hasn't arrived gets an email 3 hours before their post is announced (7 AM Eastern for post 1, 11 AM for post 2), with the announcement as the deadline
- 🧪 **Try a reminder run** without waiting for December: `POST https://localhost:7092/dev/reminders/run?at=2026-12-03T15:00:00Z` (Development only). The emails land in MailPit.

## 📖 Running a Season

- 🗓️ The **[yearly runbook](docs/runbook.md)** says what happens each year, when, and who does it: creating the season, sign-ups, reminders, Advent, and what gets deleted afterwards

## ☁️ Deploying to Azure

Production runs on **Azure Container Apps** with **Azure Database for PostgreSQL** (Flexible Server, PostgreSQL 18, Burstable B1ms, 14-day point-in-time backups; restoring is in [the runbook](docs/runbook.md)), **Application Insights**, the **Azure DNS** zone for csadvent.christmas, and **Azure Communication Services** for email. All of it is modeled in the AppHost, so one `aspire deploy` creates everything. The blurb check uses Claude through Anthropic's API, with a key (below).

Email can't be sent until the sending domain is verified, which happens after the nameserver switch below.

### 🤖 Deploying from GitHub Actions

[`deploy.yml`](.github/workflows/deploy.yml) deploys every push to `main` once CI has passed on it, and **Run workflow** in the Actions tab redeploys `main` by hand. It signs in to Azure with GitHub's OIDC token (no Azure password is stored), runs `aspire deploy`, starts the migrations job and waits for it, then checks the site. Deploys never overlap.

One-time setup (`<sub>` is the subscription ID):

1. 🪪 **An identity for the workflow**, trusted only for this repository's `production` environment. It has its own resource group, so deleting `rg-csadvent` never deletes it, and it's an **Owner** of the subscription, because `aspire deploy` creates the resource group and role assignments.

   ```bash
   az group create -n rg-csadvent-github -l centralus --subscription <sub>
   az identity create -n id-csadvent-github -g rg-csadvent-github --subscription <sub>
   # GitHub names the repository by its owner's and its own IDs, so a renamed or re-created repository can't sign in.
   subject=$(gh api repos/CodingWithCalvin/CSAdvent --jq '"repo:\(.owner.login)@\(.owner.id)/\(.name)@\(.id):environment:production"')
   az identity federated-credential create -n github-production --identity-name id-csadvent-github -g rg-csadvent-github --subscription <sub> \
     --issuer https://token.actions.githubusercontent.com --subject "$subject" --audiences api://AzureADTokenExchange
   az role assignment create --assignee-object-id <the identity's principalId> --assignee-principal-type ServicePrincipal --role Owner --scope /subscriptions/<sub>
   ```

2. 🏷️ **A `production` environment** in the repository's settings, limited to the `main` branch, with these **variables**: `AZURE_CLIENT_ID` (the identity's client ID), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION` (`centralus`), `AZURE_RESOURCE_GROUP` (`rg-csadvent`), `PG_ADMIN_USERNAME` and `OAUTH_CLIENT_ID` (the production GitHub OAuth app's).
3. 🔑 **Two secrets** in the same environment, `PG_ADMIN_PASSWORD` and `OAUTH_CLIENT_SECRET`. `gh secret set PG_ADMIN_PASSWORD --env production` asks for the value without echoing it. GitHub reserves names starting with `GITHUB_`, hence `OAUTH_`.
4. 📣 **Optional: automatic social posting** (#61). Add the credentials for any of these, and that platform can be switched to Automatic on `/manage/social` after the next deploy. Without them it stays copy and paste.
   - Mastodon: secret `MASTODON_ACCESS_TOKEN`, from an application on the account's server with the `write:statuses` scope.
   - Bluesky: secret `BLUESKY_APP_PASSWORD`, an app password for the `csadvent.christmas` account.
   - Facebook: variable `FACEBOOK_PAGE_ID` and secret `FACEBOOK_PAGE_TOKEN`, a **Page** token that never expires. It has to come from a long-lived user token, which takes a few steps:
     1. On developers.facebook.com, create an app with the use case **Manage everything on your Page**, and add the permissions `pages_show_list`, `pages_read_engagement` and `pages_manage_posts`.
     2. In the **Graph API Explorer**, pick the app, choose **Get User Access Token**, tick those three permissions, and generate it. In Facebook's dialog, select the C# Advent Page.
     3. In the **Access Token Debugger**, paste that token and press **Extend Access Token**. The first token only lasts about an hour.
     4. Back in the Graph API Explorer, with the extended token, run `GET me/accounts`. The C# Advent Page's `access_token` is `FACEBOOK_PAGE_TOKEN`, and its numeric `id` is `FACEBOOK_PAGE_ID`.
     5. Check the Page token in the Access Token Debugger: **Type: Page**, **Expires: Never**, and all three permissions.
     6. Switch the app to **Live** mode (it asks for the privacy policy, `https://csadvent.christmas/privacy`). Posts from an app in Development mode can be visible only to people with a role on it.

     Changing the Facebook password, or losing the Page role, invalidates the Page token, so check it on `/manage/social` each November.
   - X: secrets `X_API_KEY`, `X_API_KEY_SECRET`, `X_ACCESS_TOKEN` and `X_ACCESS_TOKEN_SECRET` (all four, or X stays copy and paste). X's API is paid per use from prepaid credits: in October 2026 a post with a link (every announcement) cost $0.20, so a season is about $10–15, and the daily credential check about a cent while X is Automatic. Set X back to Manual after the season to stop the checks.
     1. Sign in at [console.x.com](https://console.x.com) **as `@csadvent`** (the access token is always for the account that owns the app), accept the Developer Agreement and Policy, and describe the use: posting C# Advent's own announcements.
     2. Buy credits with a card, and set a **spending limit** per billing cycle. X's first-purchase bonus credits expire after 3 months, so buy close to the season.
     3. Create a **New App**. Copy its **API Key and Secret** straight away (they're shown once): `X_API_KEY` and `X_API_KEY_SECRET`.
     4. In the app's **User authentication settings**, set the permissions to **Read and write**, the type to **Automated App / Bot**, and `https://csadvent.christmas` wherever it asks for a website or callback URL.
     5. Generate the **Access Token and Secret**: `X_ACCESS_TOKEN` and `X_ACCESS_TOKEN_SECRET`. Do it after step 4: tokens made before the switch to Read and write stay read-only, so regenerate them if you changed the permissions later.
     6. Add the four secrets (`gh secret set X_API_KEY --env production` and so on), deploy, then on `/manage/social` press **Check**, **Send a test post** (it costs a post), and set X to **Automatic**.
5. ✍️ **Optional: the blurb check** (#128). Add the secret `ANTHROPIC_API_KEY`, an API key from the [Anthropic Console](https://console.anthropic.com) (set a monthly spend limit on its workspace while you're there; a season costs cents). Posts are checked from the next deploy. Without it they aren't, and nothing is held.

The new revision of the site starts before the migrations job runs, so keep each migration safe for the code before it: add columns and tables first, and only remove them in a later release.

### 🌐 Moving csadvent.christmas to the new site

The deploy already created the `csadvent.christmas` zone with every record in it ([`infra/dns.bicep`](src/CSAdvent.AppHost/infra/dns.bicep)): the site's A, `www` and `asuid` records, the email verification records (SPF, DKIM, DMARC), the MX and SPF records for hello@ forwarding, and the Bluesky handle's `_atproto` record. None of it is live until Hover points at the zone.

Azure only accepts a custom domain once its DNS points at the app, and only issues the free managed certificate after that. So the switch goes in this order, and the site has no HTTPS on csadvent.christmas for a few minutes between steps 2 and 4. `<rg>` is the resource group, `<env>` the Container Apps environment and `web` the app.

1. 🔀 **Switch the nameservers** at Hover to the zone's four Azure nameservers (`az network dns zone show -n csadvent.christmas -g <rg> --query nameServers -o tsv`). This takes the old site off the domain. Wait until `nslookup -type=NS csadvent.christmas` shows them.
2. 🏷️ **Add the domains** to `CustomDomains` in `src/CSAdvent.AppHost/appsettings.json`, without certificates yet, and deploy. Both domains then work over HTTP only:

   ```json
   "CustomDomains": [
     { "Name": "csadvent.christmas" },
     { "Name": "www.csadvent.christmas" }
   ]
   ```
3. 🔐 **Create the managed certificates**, then wait until `az containerapp env certificate list -n <env> -g <rg> -o table` shows them as Succeeded:

   ```bash
   az containerapp env certificate create -n <env> -g <rg> --hostname csadvent.christmas --validation-method HTTP --certificate-name csadvent-christmas
   az containerapp env certificate create -n <env> -g <rg> --hostname www.csadvent.christmas --validation-method CNAME --certificate-name www-csadvent-christmas
   ```
4. ✅ **Add the certificate names** (`"Certificate": "csadvent-christmas"` and `"Certificate": "www-csadvent-christmas"`) and deploy again. Both domains are now on HTTPS, and www redirects to csadvent.christmas.
5. 📧 **Verify the email domain**, then wait until all four show as Verified (the `communication` extension for `az` installs on first use):

   ```bash
   for type in Domain SPF DKIM DKIM2; do
     az communication email domain initiate-verification --email-service-name csadvent-email -g <rg> --domain-name mail.csadvent.christmas --verification-type $type
   done
   az communication email domain show --email-service-name csadvent-email -g <rg> --domain-name mail.csadvent.christmas --query verificationStates
   ```
6. 🔗 **Deploy again** to connect the verified domain to the Communication Service (`aspire deploy` connects it once all four checks pass). Email is sent from then on. A new domain can send 30 emails a minute and 100 an hour; ask Azure support for more if a season needs it.
7. 📮 **Set up hello@** in ImprovMX: add csadvent.christmas and an alias `hello` that forwards to the organizers.

Keep `CustomDomains` and DNS records in the AppHost, not set by hand in the portal: every deploy puts back what the model says.

## 🔏 Privacy

- 📜 **[Privacy](https://csadvent.christmas/privacy) and [terms](https://csadvent.christmas/terms)** are pages in the site, with the organizers' address (`hello@csadvent.christmas`) from `Site:ContactEmail` in `appsettings.json`. The app won't start without it.
- 📇 **Authors' email addresses are kept** after the season, so they can hear about the next one, until they ask us to delete them
- 🤖 **Posts are checked for the blurb** by Claude, through Anthropic's API, which only ever sees the public text at the top of a post
- 🗑️ **Requests and backup lists are deleted automatically** on February 1 after each season, with the emails of people who signed in but never had a spot. The calendar itself stays.
- ✏️ **Collecting something new?** Update the privacy page, and its date, in the same pull request.

## 🤝 Contributing

1. Create a branch from an up-to-date `main`, named `type/scope/short-description`
2. Use [conventional commits](https://www.conventionalcommits.org/) for commits and pull request titles
3. Open a pull request. CI builds and runs every test.
