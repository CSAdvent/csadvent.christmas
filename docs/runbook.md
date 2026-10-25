# 📖 C# Advent Yearly Runbook

What happens each year, when, and who does it. The site automates most of the season, so this is mostly what to check
and the few things that need a person. Times are Eastern, the reveal time zone.

**Owners:** the organizers, [Matt Groves](https://github.com/mgroves) (founder and host) and
[Calvin Allen](https://github.com/CalvinAllen) (also the site and its Azure hosting). "Organizers" below means either of them.

---

## 🗓️ The Year at a Glance

| When | What | Who |
|---|---|---|
| September | The home page starts counting down to sign-ups | Automatic |
| September–October | Create the season, refresh copy, run a test round, check social posting | Organizers |
| Late October | Check credentials, the domain's renewal, the alerts and Actions minutes (Every November, below) | Organizers |
| November 1 | Sign-ups open; the announcements list, past authors and the Automatic social platforms hear about it at 9 AM | Automatic, once the season is scheduled; LinkedIn and Reddit by hand from `/manage/social/signups` |
| November | Approve requests, watch the backup list | Organizers |
| Any time | Check requests to add older posts to people's accounts, and for new links to older posts (each one emails the organizers) | Organizers |
| From 7 days before each day | "Your post is due" reminders, then a last call on the day | Automatic |
| December 1–25 | Reveals at midnight, announcements at 10 AM and 2 PM, the daily email once both posts are in (from 9 AM, 2 PM at the latest) | Automatic, plus LinkedIn and Reddit by hand |
| December 1–25 | Decide on posts the blurb check can't settle, before their announcement | Organizers, when emailed |
| December 31 | The calendar moves to the archive, and the home page thanks everyone | Automatic |
| Late December–January | Write, publish and share the season's retrospective | Organizers |
| February 1 | The season's requests and backup list are deleted (authors' emails are kept) | Automatic |

---

## 🍂 September–October: Get Ready

1. **Create the season** at `/manage/seasons`, with sign-ups scheduled for November 1 (entered in Eastern time). Every
   spot starts open. Once it's scheduled:
   - the between-seasons home page says when sign-ups open, and counts down from September 1;
   - the home page's share card says "Sign-ups open November 1".

   Creating next year's season early never replaces this year's calendar.
2. **Refresh the copy** at `/manage/content`: the between-seasons message, the About page and the Other Advents
   introduction. Every save is a revision you can go back to.
3. **Start this year's Other Advents** at `/manage/other-advents` from last year's list. Remove calendars that have
   stopped, and refresh or upload any missing logos.
4. **Link returning authors** at `/manage/authors`, so they see their history on My spots. New authors are linked when
   their claim is approved.
5. **Run a test round** at `/manage/testing`:
   1. Create the test season and invite testers by GitHub login.
   2. Have them claim, add posts, drop out and join the backup list.
   3. Move "today" through the rehearsal and send what's due.
   4. **End testing** when you're done.

   Only testers and organizers ever see it, and it never posts to social media.
6. **Check social posting** at `/manage/social`:
   1. Press **Check** on Mastodon, Bluesky, Facebook and X, and **Send a test post** on each (then delete the test posts).
   2. Set each to **Automatic**.

   LinkedIn and Reddit are always copy and paste. X is paid per use, so make sure its app has credit for the season.
   If Facebook's check says a permission "must be granted before impersonating a user's page", Meta has probably limited
   the app until you answer a request in its developer portal (the README's Facebook setup has the rest).
7. **Check email:** send yourself a sample from `/manage/emails`, and send a test email to hello@csadvent.christmas from
   another address (it forwards through ImprovMX).
8. **Keep the stack current:** dependency updates, and a deploy (every merge to `main` deploys, unless it only changes
   documentation).
9. **Check the announcements list** at `/manage/announcements`. The "sign-ups are open" email goes to every confirmed
   subscriber by itself at 9 AM on the day sign-ups open, 40 an hour. Write any other news there: save a draft, send a
   test to the organizers' inbox, then send it.
10. **Post "sign-ups open soon"** from `/manage/social/signups` as the day approaches (a week out, the day before). It counts
    down by itself; post it with **Post now**, or copy it for LinkedIn and Reddit.
11. **Try the blurb check** in the test round: give a test spot a real post with the blurb and one without, move "today" to
    their day, and see each entry's page. It needs the Anthropic API key (the README's deploy setup); without it, posts
    aren't checked and nothing is held.
12. **Check the alerts** (see Alerts, below): press **Test** on the `csadvent-organizers` action group in the portal and
    make sure both organizers get it, and check the uptime check is green and alerts both of you.

---

## ✍️ November: Sign-Ups

- **Requests** come in at `/manage/requests`, oldest first. A request doesn't reserve a spot, so approve promptly,
  first come, first served. When someone's spot has gone, choose another open spot for them (their comments often name a
  second choice), or decline. Decline duplicates and requests you won't take too. Every approval and decline emails the
  author: a decline points them to the open days, or to the backup list once the calendar's full.
- **Quick approvals:** a request nobody else wants its spot for has **Approve the requested spot** right on the list.
  After deciding one on its own page, **Next pending request** takes you to the next.
- **Settle the rest:** after approving a spot that others asked for too, its request's page lists them under **Settle
  the other requests**. Approve each into another open spot, or decline it; **Decline all** does the lot.
- **Contributors withdraw** their own requests on My spots, which frees the request up for another day.
- **Entries** at `/manage/entries` show every spot's status. You can fix a name or link, put someone from the backup
  list into an open spot, or release a spot.
- **Limits:** each person can have 3 requests waiting at once, and make 5 an hour, because every request emails them and
  you. The claim page tells them. Approving or declining a request, or their withdrawing one, frees them up again.
- **Backup posters** sign up at `/backups/{year}`, and you can see them at `/manage/backups`. Point people there once
  the calendar fills up. The welcome email goes once per person and season, however often they leave and join again.
- **Authors** add their post's title and URL themselves on My spots. Nobody sends them by email.
- **Past authors** get an invitation by themselves from 9 AM on the day sign-ups open, a few at a time alongside the
  announcement (40 an hour between them), and it carries on until everyone's had one, for as long as sign-ups are open.
  `/manage/announcements` shows how many are still to go. Nothing to do.

---

## ⏰ From Late November: Reminders (Automatic)

- **"Your post is due"** emails go at 9 AM, 7, 5, 3, 2 and 1 days before an author's day, while their spot has no
  post link. The last two warn that a backup poster may take the spot.
- **Backup posters** are asked one at a time from 6 PM the evening before a day whose link is still missing. Each has
  4 hours to answer. You get an email if nobody's left, and then the spot needs you at `/manage/entries`.
- **The last call** goes out on the day, 3 hours before the post is announced (7 AM for post 1, 11 AM for post 2),
  if the link still hasn't arrived.
- **The blurb check** starts the day before each post's reveal: authors who publish early can see on My spots whether
  their post passes.
- **An author who drops out** frees their spot straight away, and the backups are asked immediately.

---

## 🎄 December 1–25: Advent

- **Reveals** happen at midnight. The day revealed next counts down on the calendar.
- **Automatic posts** go to Mastodon, Bluesky, Facebook and X at 10 AM (post 1) and 2 PM (post 2), or as soon as a late
  link arrives that day.
  - Post **LinkedIn and Reddit by hand** from `/manage/social`, then **Mark as posted**.
  - Reddit posts go to r/csadvent only, never r/csharp or r/dotnet.
  - A failed automatic post shows its error and a **Retry now** button.
- **The daily email** goes to everyone with a spot as soon as both of the day's posts are in, from 9 AM, or at 2 PM with
  whichever are, leaving out any without the blurb.
- **The blurb check** runs every 15 minutes, and only posts that pass are announced automatically.
  - **A missing blurb** emails its author (once) and asks the backups from midnight, an hour each. A backup who accepts takes
    the spot at the post's announcement unless the author adds the blurb first. If nobody has by then, you get an email: the
    post stays up but isn't announced until you decide.
  - **You get an email** when Claude is unsure, or a post couldn't be checked for 30 minutes. Open the entry's page, look
    at the post, and **Mark as passed** or **Mark as missing**.
  - **An author who won't add it** (they reply to the email): **Use a backup** on the entry's page hands the spot to the next
    backup who accepts, straight away. **Announce anyway** lets a post through without it.
- **A spot that ends up empty** can be filled from the backup list, or released, at `/manage/entries`.

---

## 🎁 After Advent

- **December 31:** the calendar leaves the home page for the archive, and the between-seasons page thanks everyone
  and links to it.
- **The retrospective (late December or early January, before February 1):** at `/manage/retrospectives`, open the
  season and press **Generate the draft**. It's a whole post written from the season's numbers: posts, first-time and
  returning authors, clicks, the ten most-read posts. Rewrite it as you like, check the preview, and **Publish**. It goes
  up at `/archive/{year}/retrospective`, linked from the season's archive page and in the RSS feed. Then share it from
  the same page (Post now on Automatic platforms, Copy for the rest), and send it to the announcements list from
  `/manage/announcements` if you like. Generate it before February 1, because the season's requests and backup list
  (two of its numbers) are deleted then.
- **February 1:** the season's claim requests (with comments) and backup list are deleted automatically, and so are
  the emails of people who signed in but never had a spot. **Authors' email addresses are kept**, on their accounts and
  their spots, so they can hear about the next season. Delete an author's address when they ask. Nothing else to do.

---

## 🔑 Every November: Credentials and Limits

- **Social posting:** each Automatic platform's credentials are checked every morning at 8 AM, and you get an email
  when one fails.
  - The **Facebook Page token** is invalidated by a password change or losing the Page role.
  - The **Mastodon token** and the **Bluesky app password** last until revoked.

  The README's deploy setup says how to get each one. Replace the secret in the repository's `production`
  environment, run the Deploy workflow, and press **Check**.
- **Email:** a sample from `/manage/emails` proves the sending domain is still connected. The site can send 30 emails
  a minute and 100 an hour unless Azure has raised that (see Email Sending Limits, below).
- **The announcements sign-up limit** (5 an IP address per 10 minutes): once someone has signed up after a deploy,
  Application Insights should have "The announcements sign-up limit counts by each visitor's public IP address." A warning
  instead means every visitor is sharing one limit, so the forwarded headers need checking (#137).
- **Database restore:** practise a point-in-time restore once a year, before December (#90).
- **The blurb check's Anthropic API key** lasts until it's revoked. Check its workspace's spend limit in the Anthropic
  Console, and replace the `ANTHROPIC_API_KEY` secret the same way as the social ones if it's ever rotated.
- **Domains:** the managed certificates for csadvent.christmas and www renew themselves while the app is running. The
  domain itself doesn't: check its expiry date and auto-renew at Hover (see below).
- **Sign-in:** sign in to the live site with GitHub. Nothing else notices a revoked OAuth secret.
- **hello@:** send it a test email from another address. Nothing else notices ImprovMX failing.
- **GitHub Actions minutes:** check the organization's usage before November (see below).

Each credential and account is described below, with where it lives and how to replace it.

---

## 🔐 Credentials and Accounts

Every secret and account the site depends on. Secrets and variables named in capitals live in the repository's
**`production` environment** (Settings → Environments → production); the deploy passes each one to the AppHost parameter
named after it. Secrets only reach the site when it deploys, so after changing one, run the Deploy workflow
(`gh workflow run deploy.yml --ref main`). Where a service lets you, make the new credential, deploy, check it works, and
only then revoke the old one.

Lines marked ✏️ **To fill in** are for the organizers: they aren't written down anywhere else.

### 🌐 The Domain, csadvent.christmas

- **Lives at:** Hover, the registrar. Its name servers point at the Azure DNS zone, which holds every record
  (`infra/dns.bicep`); nothing else is set at Hover.
- ✏️ **To fill in:** whose Hover account it's in, the expiry date, whether auto-renew is on (it should be), and the card it
  renews on.
- **If it lapses, everything stops:** the site, GitHub sign-in, the site's email (its sending domain is
  `mail.csadvent.christmas`), hello@, and the Bluesky handle.
- **What notices:** only Hover's renewal emails to the account's owner, and the uptime check's domain expiry reminder if its
  plan has one.
- **Renewing:** keep auto-renew on and the card current, and check the expiry date every November.

### 📮 hello@csadvent.christmas (ImprovMX)

- **Lives at:** an ImprovMX account, with an alias `hello` that forwards to the organizers. Its MX and SPF records are in
  `infra/dns.bicep`, and the site's address for it is `Site:ContactEmail` in `appsettings.json`.
- ✏️ **To fill in:** whose ImprovMX account it is, the plan, and where `hello` forwards.
- **If it fails:** mail to hello@ bounces or vanishes. That's every email the site sends the organizers (new requests,
  drop-outs, blurb decisions, failed credential checks, "no backups left") and replies to the site's emails.
- **What notices:** nothing. The Azure alerts don't go through hello@, so they still arrive.
- **Checking:** send hello@ a test email from another address every fall. There's no key; keep the account's sign-in (with
  two-factor authentication) somewhere both organizers can reach.

### 🐙 GitHub OAuth App (Sign-In)

- **Lives in:** variable `OAUTH_CLIENT_ID` and secret `OAUTH_CLIENT_SECRET` (AppHost parameters `github-client-id` and
  `github-client-secret`). The app itself is under a GitHub account's **Settings → Developer settings → OAuth Apps**, with
  the callback `/signin-github` on the site.
- ✏️ **To fill in:** which account or organization the OAuth app belongs to.
- **If it fails** (the secret is revoked or the app deleted): nobody can sign in, so contributors can't claim spots or add
  their posts, and organizers can't reach `/manage`. The public pages keep working.
- **What notices:** nothing. The uptime check and the alerts stay green, so sign in to the live site each November.
- **Rotating:** in the OAuth app, **Generate a new client secret** (the old one keeps working until you delete it), set
  `OAUTH_CLIENT_SECRET`, deploy, sign in to check, then delete the old secret.

### ✍️ Anthropic API Key (the Blurb Check)

- **Lives in:** secret `ANTHROPIC_API_KEY` (parameter `anthropic-api-key`). The key belongs to a workspace in the
  [Anthropic Console](https://console.anthropic.com).
- ✏️ **To fill in:** whose Anthropic organization it is, and the workspace's monthly spend limit and usage alert.
- **If it fails** (revoked, or the workspace reaches its spend limit): every post is Couldn't check, and posts aren't
  announced automatically until an organizer marks them.
- **What notices:** an email to the organizers once a post couldn't be checked for 30 minutes. Posts are only checked from
  the day before their reveal, so outside the season nothing notices.
- **Spend:** set the workspace's monthly limit to cover December (a season costs cents), with a usage notification below
  it.
- **Rotating:** create a new key in the same workspace, set the secret, deploy, press **Check now** on an entry page (the
  test season works), then revoke the old key.

### 🐘 Mastodon Access Token

- **Lives in:** secret `MASTODON_ACCESS_TOKEN` (parameter `mastodon-access-token`), for `@csadvent@mastodon.social`.
- ✏️ **To fill in:** who can sign in to the account.
- **If it fails:** automatic Mastodon posts fail, each with its error and **Retry now**. Copy and paste still works.
- **What notices:** the 8 AM credential check emails the organizers, but only while Mastodon is Automatic.
- **Rotating:** on mastodon.social, **Preferences → Development**, open the application and regenerate its access token
  (or create a new application with the `write:statuses` scope). Set the secret, deploy, and press **Check** on
  `/manage/social`.

### 🦋 Bluesky App Password

- **Lives in:** secret `BLUESKY_APP_PASSWORD` (parameter `bluesky-app-password`), for the `csadvent.christmas` account.
- ✏️ **To fill in:** who can sign in to the account.
- **If it fails, and what notices:** the same as Mastodon.
- **Rotating:** in Bluesky's **Settings → Privacy and security → App passwords**, add a new one. Set the secret, deploy,
  press **Check**, then delete the old app password.

### 📘 Facebook Page ID and Token

- **Lives in:** variable `FACEBOOK_PAGE_ID` and secret `FACEBOOK_PAGE_TOKEN` (parameters `facebook-page-id` and
  `facebook-page-token`). The token comes from the Meta app on developers.facebook.com.
- ✏️ **To fill in:** whose Facebook account has the Page role and owns the Meta app.
- **If it fails:** automatic Facebook posts fail. A Facebook password change, losing the Page role, or Meta limiting the app
  all stop the token working.
- **What notices:** the 8 AM credential check, while Facebook is Automatic.
- **Rotating:** make a new Page token as the README's Facebook setup describes, set `FACEBOOK_PAGE_TOKEN`, deploy, and press
  **Check**.

### ✖️ X Credentials

- **Lives in:** secrets `X_API_KEY`, `X_API_KEY_SECRET`, `X_ACCESS_TOKEN` and `X_ACCESS_TOKEN_SECRET` (parameters
  `x-api-key`, `x-api-key-secret`, `x-access-token` and `x-access-token-secret`). All four are needed, or X stays copy and
  paste. They come from the app on [console.x.com](https://console.x.com), owned by `@csadvent`.
- ✏️ **To fill in:** who can sign in to `@csadvent`, the card on the X developer account, its spending limit, and the
  credit left.
- **If it fails:** automatic X posts fail. Besides a revoked key, X's API is paid per use, so running out of credit stops
  it too.
- **What notices:** the 8 AM credential check, while X is Automatic (each check costs about a cent).
- **Rotating:** regenerate the keys or tokens in the app's settings, update every secret you regenerated, deploy, and press
  **Check**. Tokens made before the app had Read and write permission stay read-only.

### 🗄️ PostgreSQL Admin Username and Password

- **Lives in:** variable `PG_ADMIN_USERNAME` and secret `PG_ADMIN_PASSWORD` (parameters `pg-admin-username` and
  `pg-admin-password`). Every deploy sets the server's admin password from them and writes the connection strings to the
  Key Vault Aspire created (`postgres-kv`), which the site and the migrations job read.
- **If they're wrong:** the site can't reach the database. `/health` turns Unhealthy, and the deploy's migrations step fails.
- **What notices:** the uptime check, the error alert, and the Deploy workflow.
- **Rotating** (outside the season, because connections can fail for a minute while the new revision starts): set a new
  `PG_ADMIN_PASSWORD` and run the Deploy workflow, which changes the server's password and the connection strings
  together. Then check `/health`, and restart the `web` app's revision in the portal if it's Unhealthy. The username can't
  be changed once the server exists.

### 🪪 Azure Sign-In for GitHub Actions

- **Lives in:** no secret. Variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` name the managed
  identity `id-csadvent-github` (resource group `rg-csadvent-github`, Owner of the subscription), which trusts GitHub's
  OIDC tokens for this repository's `production` environment through its federated credential `github-production`.
- ✏️ **To fill in:** who else is an Owner of the subscription, and who pays for it.
- **If it fails:** nothing deploys. It breaks if the identity, its federated credential or its role assignment is deleted,
  or if the repository is re-created or transferred, because the credential names the repository by its ID. Renaming the
  repository, or making it public, doesn't matter.
- **What notices:** the Deploy workflow fails at "Sign in to Azure".
- **Rotating:** nothing to rotate. To rebuild it, follow step 1 of the README's deploy setup.

### ✉️ Email (Azure Communication Services)

- **Lives in:** no key. The site's managed identity (`web-identity`) has the Communication and Email Service Owner role on
  the Communication Service `csadvent-acs`, so its `email` connection string is just the endpoint.
- **If it fails:** no email goes out. That happens if the role assignment is removed, the sending domain is disconnected,
  or the sending limit is reached (below).
- **What notices:** the error alert (every failed send logs an error), and a sample from `/manage/emails`.
- **Rotating:** nothing to rotate.

### 🚨 Alert Recipients

- **Lives in:** secret `ALERT_EMAILS` (parameter `alert-emails`): the addresses Azure Monitor's alerts go to, separated by
  commas. Not a credential, but personal, so it's a secret.
- **If it's empty:** the deploy leaves the alerts out. Rules already in Azure stay as they were.
- **Changing it:** set the secret and deploy. Never edit the action group in the portal, because the next deploy puts it
  back.

### ✅ Nothing to Look After

- **Application Insights, the Key Vault and the container registry** are created by the deploy and used through managed
  identities.
- **The managed certificates** renew themselves while the app runs.
- **LinkedIn and Reddit** have no credentials: they're always copy and paste.

---

## 📬 Email Sending Limits

**The limits now:** Azure Communication Services lets C# Advent send **30 emails a minute and 100 an hour**, the default
for a custom domain. They're **per subscription**, so everything the site sends shares them:

- claim emails (two per claim: one to the contributor, one to the organizers; then one more when it's approved or
  declined). One person can have 3 requests waiting and make 5 an hour, so nobody can use the limit up alone (#137);
- announcements and past-author invitations;
- post link reminders and the last call;
- the daily email;
- backup asks;
- blurb emails;
- the organizers' notices.

Over the limit, Azure refuses with 429 (Too Many Requests). Claim emails aren't retried: they're logged, which trips the
error alert. Only announcements and invitations are budgeted (`Announcements:PerHour`, 40), and
`Reminders:PauseBetweenEmails` (2 seconds) only paces one loop on one replica. So on sign-up day, about 30 claims in the
first hour plus the announcement can reach the limit.

The current limits are in the portal: the Email Communication Service `csadvent-email` → **Provision domains** →
`mail.csadvent.christmas` shows **Email send quota per minute** and **per hour**.

**Asking for more** (#136): it's free, reviewed by hand, and can take up to 72 hours, so ask well before November.

1. Copy the questionnaire from [Quota increase for email domains](https://learn.microsoft.com/azure/communication-services/concepts/email/email-quota-increase)
   into a `.txt` file and answer it. It asks for company details: C# Advent is a personal, volunteer-run community
   project, so describe it as that, with csadvent.christmas as the website. The rest is:
   - the C# Advent subscription ID;
   - the Communication Service `csadvent-acs`;
   - the sending domain `mail.csadvent.christmas`, already in use;
   - transactional emails, plus announcements people sign up for;
   - the peak rates you need;
   - where the addresses come from (authors who signed up on the site with their GitHub account, and people who confirmed
     a subscription) and how unsubscribes are handled (a one-click link that's honored straight away).
2. In the portal, go to **Help + support → Create a support request**. Choose the issue type **Service and subscription
   limits (quotas)**, the C# Advent subscription, and the quota type **Azure Communication Services Email: Sending
   Limits**.
3. On **Additional details**, enter the resource group, the email domain and the new limit, attach the questionnaire, and
   create the request. An automated email confirms it within minutes.
4. **Ask for December's peak**, not November's. The daily email, reminders and backup asks all go out around 9 AM
   Eastern, and sign-up day brings about 60 claim emails in the first hour on top of the announcement.
5. **Approval depends on sender reputation.** Keep the failure rate under 2% (Microsoft's bar). The docs also recommend an
   MX record on the sending domain; `mail.csadvent.christmas` doesn't have one (`dns.bicep` only has the apex's ImprovMX
   records).
6. **If it's refused**, sending emails through an outbox that respects the limit becomes urgent.
7. **Once it's granted**, record the new limits here and in CLAUDE.md, and raise `Announcements:PerHour` and lower
   `Reminders:PauseBetweenEmails` to match.

- ✏️ **To fill in:** when it was requested, the support request's number, and the outcome.

**Until it's granted**, watch `/manage/requests` on sign-up day rather than waiting for the email notices.

---

## 💸 GitHub Actions Minutes

The repository is private, and the CodingWithCalvin organization is on GitHub's Free plan: **2,000 Linux minutes a month**,
shared with the organization's other private repositories. Each job is billed in whole minutes, rounded up. When they run
out, CI stops, and so do deploys, because they wait for CI.

**What a change costs** (October 2026): about 4 minutes of CI for each push to a pull request, plus 2 for the commit
lint (two jobs of a minute each), then 4 more for CI on `main` after the merge and about 7 for the deploy. So a pull request
merged after one push is about 17 minutes. October 25 had 8 deploys, and recent use was about 180 minutes a day: at that
pace November's allowance runs out mid-month.

**Already cut** (#140):

- A new push to a pull request cancels the CI run and the commit lint it replaces.
- A pull request that only changes `README.md`, `CLAUDE.md` or `docs/` skips the build and the tests. Its "Build and test"
  check still passes, so it can never hold up a required check.
- A push to `main` that only changes those doesn't run CI or deploy at all. Run the Deploy workflow by hand if you want
  one anyway.

**The decision (organizers):** pick one before November.

1. **Make the repository public.** Actions minutes on GitHub's standard runners are free for public repositories. Before
   switching:
   - Scan the whole history for secrets with a scanner such as [gitleaks](https://github.com/gitleaks/gitleaks). The real
     secrets live in the `production` environment, not the repository, but check anyway.
   - Read what becomes public: the docs name the subscription ID, the resource names and the deploy identity's federated
     subject (none of them secret), and every commit shows its author's email address.
   - Afterwards, only `main` can deploy (the `production` environment's rule), so pull requests from forks never see the
     secrets, and GitHub asks before running workflows for first-time contributors. Branch protection becomes available
     too (a private repository on the Free plan can't have it): requiring "Build and test" is safe with the documentation
     skip above.
   - Switch it in **Settings → General → Danger Zone → Change visibility**.
2. **Or set an Actions budget**, so CI keeps running past the free minutes: the organization's **Settings → Billing and
   licensing → Budgets and alerts → New budget**, for Actions, with email alerts. The issue suggested $10–20 a month. At
   October's pace that's 3,000 or more extra minutes a month, so check what that costs at GitHub's current price for Linux
   minutes before picking the amount, and whether the budget should stop usage (which stops CI again) or only warn.

Either way, check the organization's **Settings → Billing and licensing → Usage** (filtered to Actions) before November,
and again in early December.

- ✏️ **To fill in:** which option, when, and the budget amount if there is one.

---

## 🚨 Alerts

Two things watch the site and email the organizers, and neither depends on the site's own email (which goes through
hello@, so it can't warn anyone about itself):

- **An uptime check outside Azure** asks `https://csadvent.christmas/health` every few minutes. It's set up by hand (below).
- **Azure Monitor alerts**, defined in [`infra/alerts.bicep`](../src/CSAdvent.AppHost/infra/alerts.bicep) and created by the
  deploy once the `ALERT_EMAILS` secret is set (the README's deploy setup). They go to every address in it, from Azure's
  own `azure-noreply@microsoft.com`, so add that to your contacts. Each address gets an email from Azure when it's added
  to the `csadvent-organizers` action group. To change who gets them, change the secret and run the Deploy
  workflow; a change made in the portal is undone by the next deploy.

The alerts that keep firing (errors, database) email once when they start and again when they clear. In the portal they're
under **Monitor → Alerts**, and **Monitor → Alerts → Alert rules** lists the rules. The action group's **Test** button sends
a sample to everyone.

### 🔔 What Each Alert Means

- **"C# Advent: errors in the site's logs"** (`csadvent-errors`, checked every 15 minutes): the site or a background job
  logged an error. Usually an email that couldn't be sent, a background run that failed (reminders, backups, social posting,
  announcements, retention, logos), or a page that crashed.
  1. Open the query results linked from the email, or Application Insights → **Logs** with
     `union traces, exceptions | where timestamp > ago(1h) and severityLevel >= 3 | order by timestamp desc`, and read the
     message and `customDimensions.CategoryName`.
  2. Background runs try again by themselves (every few minutes, or hourly for retention), so one error that clears is
     usually nothing. One that keeps coming back needs a fix.
  3. On sign-up day, emails refused with 429 are the email sending limit (below).

  The duplicate inserts that the reminders, backups and announcements expect, which EF Core logs as errors all day, are left
  out of this alert until #157 stops them being logged.
- **Database storage over 80%** (`csadvent-postgres-storage`): the server has 32 GB and doesn't grow by itself. At 95% it
  turns read-only, and the site stops saving anything. Raise `StorageSizeInGB` in `AppHost.cs` and deploy (storage can grow
  but never shrink), then look for what grew:

  ```sql
  select relname, pg_size_pretty(pg_total_relation_size(relid)) from pg_statio_user_tables
  order by pg_total_relation_size(relid) desc limit 10;
  ```
- **Database CPU credits below 30** (`csadvent-postgres-cpu-credits`): the server is Burstable B1ms, which saves up credits
  while it's quiet and spends them when it's busy. With none left, it's held to 20% of a core and the site slows to a crawl.
  Look for what's keeping it busy (the server's **CPU percent** metric, and requests in Application Insights: a crawler or
  a loop). If December really needs more, change the `Sku` in `AppHost.cs` (B2s is the next size up, at about twice the
  price) and deploy. Changing it restarts the server for a minute or two, and Azure warns that restarts are slow once the
  credits are gone, so act on this warning rather than at zero.
- **More than 30 database connections** (`csadvent-postgres-connections`): B1ms allows 50, and Azure keeps some of them.
  A deploy briefly runs the old and new revisions side by side, so one alert that clears within 15 minutes is fine.
  Otherwise see who's connected:

  ```sql
  select usename, application_name, state, count(*) from pg_stat_activity group by 1, 2, 3 order by 4 desc;
  ```
- **Service Health** (`csadvent-service-health`): Azure reported an outage, planned maintenance, a security notice or
  something to act on, for a service in Central US or a global one (DNS, email, Azure's sign-in). Read it in the portal under
  **Service Health**. Outages are Azure's to fix: tell people on social media if the site is affected. Planned database
  maintenance restarts the server for a few minutes.
- **The uptime check** says the site is down, or its certificate is about to expire:
  1. Open the site and `/health` yourself.
  2. **Unhealthy**: the site can't reach the database (look at the server in the portal, and the database alerts).
  3. **No answer**: look at the `web` container app's revisions in the portal, and at the last deploy in GitHub Actions.

### 📟 Setting Up the Uptime Check (by Hand, Once)

Use the free tier of [UptimeRobot](https://uptimerobot.com) or [Better Stack](https://betterstack.com/uptime). Both organizers
need to be on it.

1. Add an **HTTP(S) monitor** for `https://csadvent.christmas/health` every 5 minutes. It answers 200 when everything's
   fine and 503 when the database is down, and never redirects.
2. Turn on the **certificate expiry** reminder if the plan has one, and the domain expiry one too.
3. Alert **both organizers** by email and by the service's phone app.
4. ✏️ **To fill in:** which service, whose account, and who gets its alerts.

### 💰 A Budget Alert (Optional, by Hand)

Budgets are set on the subscription, outside the deploy. In the portal: **Cost Management → Budgets → Add**, monthly, with
alerts to both organizers at 80% and 100%. It's free, and only warns: nothing is stopped.

- ✏️ **To fill in:** the monthly amount, and whether it's set up.

---

## 🧭 If Something Breaks

- **Alerts:** what each one means is under Alerts, above.
- **The site:** every deploy runs the database migrations job and checks the site. A new revision that can't reach the
  database never takes traffic, because the health probes keep the old one serving.
- **Logs and telemetry:** Application Insights, in the `rg-csadvent` resource group.
- **Emails not arriving:** check `/manage/emails` (send a sample) and the Communication Service in Azure.
- **Every post says "Couldn't check":** Claude isn't answering. Each entry's page says why (a rate limit, an expired key,
  or the workspace's spend limit). Posts aren't announced automatically meanwhile, so mark the day's posts as passed (or
  announce them anyway) by hand.
- **A wrong post on the calendar:** fix it at `/manage/entries`. An edited title gets a fresh share card.
- **Lost or damaged data:** restore the database to a point before it happened. See below.

---

## 🗄️ Restoring the Database

Every season since 2017 lives in one PostgreSQL Flexible Server, `postgres-54yzcow3wctne` in `rg-csadvent` (Central US).
Azure backs it up automatically, and it can go back to **any second in the last 14 days**, up to a few minutes ago.
Backups stay in Central US: there's no geo-redundant copy.

A restore always makes a **new** server, and production isn't touched. The test on October 25, 2026 restored production
from one minute before into a new server in **7 minutes 10 seconds**, with every season and that minute's latest migration.
Deleting the server took about a minute.

Pass `--subscription 4fab8317-7755-44bc-9063-1e8cc1c6867d` (C# Advent) to every command, because this may not be your
default subscription.

1. **Restore to a new server**, at a time (UTC) just before the problem:

   ```bash
   az postgres flexible-server restore --resource-group rg-csadvent \
     --name csadvent-restore-YYYYMMDD --source-server postgres-54yzcow3wctne \
     --restore-time 2026-12-05T14:30:00Z --subscription 4fab8317-7755-44bc-9063-1e8cc1c6867d
   ```

   It copies the source's admin login and password. In the portal, it's the server's **Restore** button.

2. **Look at it.** Give the **new** server a password of its own, so you don't need production's, and let your IP in. Then
   connect with any `psql`; Docker's works.

   ```bash
   az postgres flexible-server update -g rg-csadvent -n csadvent-restore-YYYYMMDD --admin-password '<new password>' --subscription …
   az postgres flexible-server firewall-rule create -g rg-csadvent -n csadvent-restore-YYYYMMDD --rule-name restore-check \
     --start-ip-address <your IP> --end-ip-address <your IP> --subscription …
   docker run --rm -it postgres:18-alpine psql "host=csadvent-restore-YYYYMMDD.postgres.database.azure.com dbname=csadvent user=<admin login> sslmode=require"
   ```

   Then check what's there:

   ```sql
   select season_year, count(*) as spots, count(post_url) as posts from slots group by season_year order by season_year;
   select * from "__EFMigrationsHistory" order by 1 desc limit 3;
   ```

3. **Get the data back.**
   - **Usually only what was lost:** copy those rows out of the restored server, with `psql`'s `\copy`, and into
     production.
   - **The whole database** (untested):
     1. Stop the site by deactivating the `web` app's revision in the portal, so nothing writes in between.
     2. Add the same firewall rule to production for the copy.
     3. Copy the database in. Production's admin login and password are the `pg-admin-username` and `pg-admin-password`
        values given to the deploy.

        ```bash
        pg_dump -Fc --no-owner -f csadvent.dump "host=csadvent-restore-YYYYMMDD.postgres.database.azure.com dbname=csadvent user=<admin login> sslmode=require"
        pg_restore --clean --if-exists --no-owner -d "host=postgres-54yzcow3wctne.postgres.database.azure.com dbname=csadvent user=<admin login> sslmode=require" csadvent.dump
        ```

     4. Remove production's firewall rule, and turn the site back on.

     The dump has the schema and the migration history too, so there's no need to run the migrations job.
   - **Don't point the site at the restored server.** The AppHost owns the server's name and connection string, so the
     next deploy would point it back, and the new server would be missing from the deploy's model.

4. **Delete the restored server** when you're done. It costs money while it exists, and its firewall rule lets your IP in:

   ```bash
   az postgres flexible-server delete -g rg-csadvent -n csadvent-restore-YYYYMMDD --yes --subscription …
   ```

   **Check the name** before you press Enter: deleting `postgres-54yzcow3wctne` would delete production.
