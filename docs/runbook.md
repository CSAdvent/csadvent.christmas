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
| Late October | Check credentials, the domain's renewal and the alerts (Every November, below) | Organizers |
| November 1 | Sign-ups open; the announcements list, past authors and the Automatic social platforms hear about it at 9 AM | Automatic, once the season is scheduled; LinkedIn and Reddit by hand from `/manage/social/signups` |
| November | Approve requests, watch the backup list | Organizers |
| Any time | Check requests to add older posts to people's accounts, and for new links to older posts (each one emails the organizers) | Organizers |
| Any time | Posts found dead are emailed to the organizers at 8 AM: give moved posts their new link on the dead links page (readers already get a saved copy where there is one) | Automatic check; organizers fix |
| From 7 days before each day | "Your post is due" reminders, then a last call on the day | Automatic |
| By November 30 | Raise the site to 2 replicas and pause Dependabot, in one pull request, and merge any database migrations (December, below) | Organizers |
| December 1–26 | Schema freeze: no pull requests with database migrations (December, below) | Organizers |
| December 1–25 | Reveals at midnight, announcements at 10 AM and 2 PM, the daily email once both posts are in (from 9 AM, 2 PM at the latest) | Automatic, plus LinkedIn and Reddit by hand |
| December 1–25 | Decide on posts the blurb check can't settle, and keep an author before a backup takes their spot if the check got it wrong, before their announcement | Organizers, when emailed |
| December 27 | Back to 1 replica, and Dependabot back on (After Advent, below) | Organizers |
| December 31 | The calendar moves to the archive, and the home page turns to next year's season and thanks everyone for this one | Automatic |
| Late December–January | Write, publish and share the season's retrospective | Organizers |
| January | Save the season to the archive files in the repository (After Advent, below) | Organizers |
| February 1 | The season's requests and backup list are deleted (authors' emails are kept) | Automatic |
| February 2027, once | Revoke the data protection keys from before they were encrypted (After Advent, below) | Organizers |

---

## 🍂 September–October: Get Ready

1. **Create the season** at `/manage/seasons`, with sign-ups scheduled for November 1 (entered in Eastern time). Every
   spot starts open. Once it's scheduled:
   - the between-seasons home page says when sign-ups open, and counts down from September 1;
   - the home page's share card says "Sign-ups open November 1";
   - the between-seasons home page and `/announcements` offer add-to-calendar files for sign-ups opening and December 1.

   Creating next year's season early never replaces this year's calendar.
2. **Refresh the copy** at `/manage/content`: the between-seasons message, the About page and the Other Advents
   introduction. Every save is a revision you can go back to.
3. **Start this year's Other Advents** at `/manage/other-advents` from last year's list. Remove calendars that have
   stopped, and refresh or upload any missing logos.
4. **Link returning authors** at `/manage/authors`, so they see their history on My spots and all their posts share one
   public author page. New authors are linked when their claim is approved.
5. **Run a test round** at `/manage/testing`:
   1. Create the test season and invite testers by GitHub login.
   2. Have them claim, add posts, drop out and join the backup list.
   3. Move "today" through the rehearsal and send what's due. Watch the rehearsal's **Today** panel (`/manage?year=9000`,
      linked from the testing page) and its morning digest in hello@, as you will in December.
   4. **End testing** when you're done.

   Only testers and organizers ever see it, and it never posts to social media.
6. **Check social posting** at `/manage/social`:
   1. Press **Check** on Mastodon, Bluesky, Facebook and X, and **Send a test post** on each (then delete the test posts).
   2. Set each to **Automatic**.

   LinkedIn and Reddit are always copy and paste. X is paid per use, so make sure its app has credit for the season.
   If Facebook's check says a permission "must be granted before impersonating a user's page", Meta has probably limited
   the app until you answer a request in its developer portal ([the deployment guide's Facebook section](deployment.md#facebook) has the rest).
7. **Check email:** send yourself a sample from `/manage/emails` (it goes through the email queue, so it proves both),
   check `/manage/emails/queue` has nothing failed, and send a test email to hello@csadvent.christmas from another address
   (it forwards through ImprovMX).
8. **Keep the stack current:** Dependabot opens a pull request each Monday for the NuGet packages and one for the
   workflows' actions, each update grouped into one (`.github/dependabot.yml`; EF Core, Npgsql and Aspire wait for the .NET
   11 GA work, #55). Merge them before November, and deploy (every merge to `main` deploys, unless it only changes
   documentation).
9. **Check the announcements list** at `/manage/announcements`. The "sign-ups are open" email goes to every confirmed
   subscriber by itself at 9 AM on the day sign-ups open: queued for everyone at once, and sent as list mail, after the
   claim emails (see Email Sending Limits). Write any other news there: save a draft, send a test to the organizers'
   inbox, then send it.
10. **Post "sign-ups open soon"** from `/manage/social/signups` as the day approaches (a week out, the day before). It counts
    down by itself; post it with **Post now**, or copy it for LinkedIn and Reddit.
11. **Try the blurb check** in the test round: give a test spot a real post with the blurb and one without, press **Check my
    post** on My spots as the tester, then move "today" to their day, **send what's due** (it checks the test season's posts
    first), and see each entry's page. It needs the Anthropic API key ([the deployment guide](deployment.md)); without it, posts aren't
    checked and nothing is held.
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
- **Past authors** get an invitation by themselves from 9 AM on the day sign-ups open, after the announcement, only as
  fast as the email queue has room for them, and it carries on until everyone's had one, for as long as sign-ups are open.
  `/manage/announcements` shows how many have been invited, how many are still in the queue and how many are still to go.
  Nothing to do.
- **The "sign-ups are open" posts** go out by themselves at 9 AM on the Automatic platforms, once. One that doesn't go out
  isn't tried again, and you get an email: open `/manage/social/signups` and press **Post now** (if it says "Did it go
  out?", look for it on the account first, and **Mark as posted** if it's there).
- **Sign-up day's email** (see Email Sending Limits): the claim emails go first, and the announcement and invitations go a
  minute at a time with what's left of each hour. Beyond about 40 claims in an hour, some claim emails wait for the next
  hour, so keep an eye on `/manage/requests` rather than waiting for the "New request" emails, and on
  `/manage/emails/queue`: how much is waiting, the hour's sends against 90, and anything failed.

---

## ⏰ From Late November: Reminders (Automatic)

- **"Your post is due"** emails go at 9 AM, 7, 5, 3, 2 and 1 days before an author's day, while their spot has no
  post link. The last two warn that a backup poster may take the spot.
- **Backup posters** are asked one at a time from 6 PM the evening before a day whose link is still missing. Each has
  4 hours to answer. You get an email if nobody's left, and then the spot needs you at `/manage/entries`. If the spot
  needs a backup again later, for a new author or a new reason (dropped in November, then the new author's link is
  missing in December, say), everyone's asked again, and you hear again if nobody takes it (#153).
- **The last call** goes out on the day, 3 hours before the post is announced (7 AM for post 1, 11 AM for post 2),
  if the link still hasn't arrived.
- **The blurb check** starts the day before each post's reveal, and authors can press **Check my post** on My spots any
  time before their day (10 times an hour each). Before the reveal it's only for them: nobody's emailed and no backup is
  asked. An author who asks about a page we can't read (one that needs JavaScript, say) may email you to sort it out early:
  look at the post, and **Mark as passed** on its entry page if the blurb is there.
- **An author who drops out** frees their spot straight away, and the backups are asked immediately.

---

## 🎄 December 1–25: Advent

- **Before December 1** (by November 30), in one pull request, so it's one deploy:
  - **Two replicas:** set `Web:MinReplicas` to `2` in `src/CSAdvent.AppHost/appsettings.json` (#158). With one, a replica
    restarting (a crash, or Azure moving it) takes the site down for a minute. The second replica is billed for as long as
    it runs, which is why it's only for December. Merging deploys it; afterwards the `web` container app's **Scale** page
    in the portal shows a minimum of 2. Don't change it in the portal instead: the next deploy puts back what
    `appsettings.json` says.
  - **Pause Dependabot:** set both `open-pull-requests-limit` lines in `.github/dependabot.yml` to `0`, so no dependency
    updates arrive mid-season. Security alerts still show in the repository's **Security** tab.
- **Schema freeze, December 1 to 26** (#151): no pull request that adds a migration (a new file in
  `src/CSAdvent.Web/Data/Migrations`) merges in those days; merge them by November 30, or wait for December 27. Every deploy
  runs its migrations while the previous version is serving, and a migration that goes wrong mid-season is the hardest
  thing to undo (A Bad Migration, below). Code changes still deploy as usual. If a fix truly needs one, keep it to adding
  a table or a nullable column, and merge it after the day's 2 PM announcement.
- **Start each day with the morning digest**, which reaches hello@ at 8 AM (#171): today's and tomorrow's spots (whether
  each post is in, its blurb check, and where it stands on every platform), the backups asked or standing by, posts and
  emails that didn't go out, and what's waiting. **Today** at the top of `/manage` shows the same at any time, linking to
  each spot's entry page, the day's announcements and the email queue. Tomorrow's posts aren't named in the email before
  their reveal, so open the panel to see them. During a rehearsal, the test season gets a digest of its own each rehearsal
  day, and its panel is `/manage?year=9000`.
- **Reveals** happen at midnight. The day revealed next counts down on the calendar.
- **Automatic posts** go to Mastodon, Bluesky, Facebook and X at 10 AM (post 1) and 2 PM (post 2), or as soon as a late
  link arrives that day.
  - Post **LinkedIn and Reddit by hand** from `/manage/social`, then **Mark as posted**.
  - Reddit posts go to r/csadvent only, never r/csharp or r/dotnet.
  - **A failed automatic post** shows its error. It's tried again after 5 and 10 minutes, then stops, and you get an email
    ("Facebook didn't post …"). Fix what it says, then press **Retry now**, or post it by hand and **Mark as posted**.
  - **"Did it go out?"** (in amber, and an email asking the same) means the platform never said: the post was sent, then
    it timed out, the connection dropped or the platform had a server error, or the app restarted mid-post. It may be up,
    so it isn't tried again by itself. Look for it on C# Advent's account: if it's there, **Mark as posted** (paste its
    link); if it isn't, **Retry now**. Bluesky looks for an earlier try's post before posting again, so it just retries.
  - **Links go straight to the post:** a social post (or RSS item) links the entry's page on our site, so the preview shows
    our card, but readers following it land on the author's post, counted as a click. Signed in as an organizer, you get
    the entry's page instead; open a link in a private window to see what readers get.
- **The daily email** goes to everyone with a spot as soon as both of the day's posts are in, from 9 AM, or at 2 PM with
  whichever are, leaving out any without the blurb. Announcement subscribers who asked for the daily posts are queued the
  same email at the same time, and get it after the authors, as list mail (before any announcement), until midnight, when
  any still waiting are dropped (see Email Sending Limits). `/manage/announcements` shows how many readers there are and
  how long a day's send takes.
- **Watch the email queue around 9 AM** at `/manage/emails/queue`: the authors' daily email, post link reminders and the
  readers' copies all go then, within the limits, and backup asks and blurb emails go ahead of them. Anything failed has
  its error and a **Retry** button; the error alert means the queue gave up on one.
- **The blurb check** runs every 15 minutes, and only posts that pass are announced automatically.
  - **A missing blurb** emails its author (once) and asks the backups from midnight, an hour each. A backup who accepts takes
    the spot at the post's announcement unless the author adds the blurb first. If nobody has by then, you get an email: the
    post stays up but isn't announced until you decide.
  - **"A backup is standing by"**: once a backup accepts, you get an email saying when they take the spot (the announcement,
    or an hour after the email if that's later). If the check got it wrong, **Keep the author** opens the entry's page:
    **Mark as passed** if the blurb is there, or **Announce anyway** to keep them without it. To let the backup have it, do
    nothing. (Not when you marked it missing or chose **Use a backup** yourself.)
  - **You get an email** when Claude is unsure, or a page has no link back and doesn't seem to be the post (it needs
    JavaScript, or it's a bot check or a "not found" page). Open the entry's page, look at the post, and **Mark as passed**
    or **Mark as missing**.
  - **Posts that don't load** count as not published yet until 3 hours before their announcement (7 AM for post 1, 11 AM for
    post 2), because many authors publish that morning. If one still doesn't load by then, you get a "couldn't check"
    email half an hour later.
  - **An author who won't add it** (they reply to the email): **Use a backup** on the entry's page hands the spot to the next
    backup who accepts, straight away. **Announce anyway** lets a post through without it.
- **A spot that ends up empty** can be filled from the backup list, or released, at `/manage/entries`.

---

## 🎁 After Advent

- **December 27:** undo the December changes in one pull request: `Web:MinReplicas` back to `1`, and both
  `open-pull-requests-limit` lines in `.github/dependabot.yml` back to `5`. Then merge the dependency updates that arrive.
  The schema freeze is over, so migrations can merge again.
- **December 31:** the calendar leaves the home page for the archive. The between-seasons page leads with next year's
  season ("C# Advent 2027", how to take part, the announcements box), then thanks everyone for this one, linking its
  archive, and its retrospective once that's published. Create next year's season whenever you like: the page shows when
  its sign-ups open as soon as they're scheduled.
- **Dead links (from December 26, all year):** once a season is over, its posts join the dead-link check, a few posts
  every 15 minutes, each about once a month. A post that fails three checks in a row, days apart, is dead, and readers
  following its link get the Wayback Machine's saved copy, if it has one. The organizers get one email at 8 AM Eastern
  ("older C# Advent posts are gone") listing the posts found dead since the day before, with what the check found and
  whether readers get a saved copy; the first pass over a season's posts can list dozens. A failed check is never an
  alert. From the email, or every so often, open `/manage/authors/dead-links` (linked from Authors):
  - **A post that moved:** give it its **New link** (search for the title, or ask the author). Every link we've published
    follows it at once.
  - **No saved copy:** paste one of your own into **Saved copy** if you find one (archive.today, say), or leave it.
  - **A false alarm** (a site that only refuses checks like ours): **It works**. **It's gone** is for a page the check
    can't tell is gone, such as a domain that's been sold.
- **The retrospective (late December or early January, before February 1):** at `/manage/retrospectives`, open the
  season and press **Generate the draft**. It's a whole post written from the season's numbers: posts, first-time and
  returning authors, clicks, the ten most-read posts. Rewrite it as you like, check the preview, and **Publish**. It goes
  up at `/archive/{year}/retrospective`, linked from the season's archive page and in the RSS feed. Then share it from
  the same page (Post now on Automatic platforms, Copy for the rest), and send it to the announcements list from
  `/manage/announcements` if you like. Generate it before February 1, because the season's requests and backup list
  (two of its numbers) are deleted then.
- **Save the season to the archive files (January):** on `/manage/seasons`, press the finished season's
  **Download {year}.json** and save it as `src/CSAdvent.Web/Data/Archive/{year}.json`, then commit it in a pull
  request. It's the calendar as shown (authors, links, titles, summaries; no contact details), in the same format as
  2017–2025, and any new database is seeded with it, so the season no longer depends on the database's 35-day backups.
  If an author asks for their entry to be taken down later, remove it from that file too.
- **Move the workflows to a newer runner (January):** every workflow runs on `ubuntu-24.04`, pinned so GitHub's move of
  `ubuntu-latest` to a new Ubuntu couldn't land mid-season (#177). Outside the season, try the newer image in a pull
  request (its CI run tests it), and change all three workflows together.
- **February 1:** the season's claim requests (with comments) and backup list are deleted automatically, and so are
  the emails of people who signed in but never had a spot (unless they're waiting to hear about a request for an older
  post). **Authors' email addresses are kept**, on their accounts and
  their spots, so they can hear about the next season. Delete an author's address when they ask. Nothing else to do.
- **Once, in February 2027: revoke the data protection keys from before #193.** Since #193 the keys that protect sign-in
  cookies, antiforgery tokens and the links in emails are encrypted with a key in Key Vault, but the ones made before it
  aren't, and they're kept so nobody was signed out mid-season. Until they're revoked, anyone with a copy of the database
  could still use them to make an organizer's cookie or an unsubscribe link. Revoking them breaks the links in emails sent
  before the encrypted key took over (two days after #193's deploy), which by February are well past the 30 days an
  unsubscribe link has to keep working; anyone can still unsubscribe from a newer email or their profile. Nobody is signed
  out by then: sign-ins move to the new key as they're renewed, and organizers' last 12 hours.
  1. Connect to **production** with `psql`, as in Restoring the Database, step 2 (a firewall rule for your IP, and the
     admin login and password given to the deploy). List the keys, oldest first:

     ```sql
     select k.id, (xpath('/key/creationDate/text()', k.xml::xml))[1]::text as created,
            (xpath('/key/activationDate/text()', k.xml::xml))[1]::text as active_from,
            k.xml like '%encryptedSecret%' as encrypted
     from data_protection_keys k where k.friendly_name like 'key-%' order by created;
     ```

     Every key since the first encrypted one should be encrypted, and that one should have been active for more than 30
     days. If not, stop and find out why.
  2. Revoke every key made before the first encrypted one (data protection's own format for revoking keys in bulk; it
     inserts nothing if there's no encrypted key):

     ```sql
     insert into data_protection_keys (friendly_name, xml)
     select 'revocation-before-encryption',
            '<revocation version="1"><revocationDate>'
              || to_char((min((xpath('/key/creationDate/text()', k.xml::xml))[1]::text::timestamptz) - interval '1 second') at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
              || '</revocationDate><key id="*" /><reason>Made before the keys were encrypted at rest (#193)</reason></revocation>'
     from data_protection_keys k
     where k.friendly_name like 'key-%' and k.xml like '%encryptedSecret%'
     having count(*) > 0;
     ```

  3. Restart the `web` app's active revision in the portal, so every replica reloads its keys now rather than within a
     day, then sign in to check. Remove your firewall rule.

  To undo it, `delete from data_protection_keys where friendly_name = 'revocation-before-encryption';` and restart again.
  Then delete this step.

---

## 🔑 Every November: Credentials and Limits

- **Social posting:** each Automatic platform's credentials are checked every morning at 8 AM, and you get an email
  when one fails.
  - The **Facebook Page token** is invalidated by a password change or losing the Page role.
  - The **Mastodon token** and the **Bluesky app password** last until revoked.

  [The deployment guide](deployment.md) says how to get each one. Replace the secret in the repository's `production`
  environment, run the Deploy workflow, and press **Check**.
- **Email:** a sample from `/manage/emails` proves the sending domain is still connected and the email queue is
  sending. The site can send 30 emails a minute and 100 an hour, and the queue keeps it under that (see Email Sending
  Limits, below).
- **The announcements sign-up limit** (5 an IP address per 10 minutes): once someone has signed up after a deploy,
  Application Insights should have "The announcements sign-up limit counts by each visitor's public IP address." A warning
  instead means every visitor is sharing one limit, so the forwarded headers need checking (#137).
- **Database restore:** practise a point-in-time restore once a year, before December (#90).
- **The blurb check's Anthropic API key** lasts until it's revoked. Check its workspace's spend limit in the Anthropic
  Console, and replace the `ANTHROPIC_API_KEY` secret the same way as the social ones if it's ever rotated.
- **Domains:** the managed certificates for csadvent.christmas and www renew themselves while the app is running. The
  domain itself doesn't: check its expiry date and auto-renew at Hover (see below). csadvent.christmas's certificate is
  validated over plain http, which reaches the app since #165 (`AllowInsecure`, so http://www. is one redirect): check
  `az containerapp env certificate list` shows both as Succeeded, and if a renewal ever fails, turning `AllowInsecure`
  off in `AppHost.cs` is the first thing to try.
- **Sign-in:** sign in to the live site with GitHub. Nothing else notices a revoked OAuth secret.
- **hello@:** send it a test email from another address. Nothing else notices ImprovMX failing.
- **Dependency alerts:** the repository's **Security → Dependabot** tab lists known vulnerabilities in the packages and
  actions. Fix any before December. (It needs Dependabot alerts on, under **Settings → Code security**.)
- **The database's maintenance window:** Azure patches the server on Sundays from 08:00 UTC, 3 AM Eastern (#158), and
  emails a notice 5 days ahead (the Service Health alert). If one lands on a day that matters, the server's **Maintenance**
  page in the portal can **Reschedule** it.

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
- **Account:** Calvin's Hover account. Auto-renew is on, with Calvin's card on file; Hover's domain page shows the next
  renewal date.
- **If it lapses, everything stops:** the site, GitHub sign-in, the site's email (its sending domain is
  `mail.csadvent.christmas`), hello@, and the Bluesky handle.
- **What notices:** only Hover's renewal emails to the account's owner, and the uptime check's domain expiry reminder if its
  plan has one.
- **Renewing:** keep auto-renew on and the card current, and check the expiry date every November.

### 📮 hello@csadvent.christmas (ImprovMX)

- **Lives at:** an ImprovMX account, with an alias `hello` that forwards to the organizers. Its MX and SPF records are in
  `infra/dns.bicep`, and the site's address for it is `Site:ContactEmail` in `appsettings.json`.
- **Account:** Calvin's ImprovMX account, on the free plan. `hello` forwards to Calvin only, so Matt doesn't get the
  site's organizer emails.
- **If it fails:** mail to hello@ bounces or vanishes. That's every email the site sends the organizers (new requests,
  drop-outs, blurb decisions, failed credential checks, "no backups left") and replies to the site's emails.
- **What notices:** nothing. The Azure alerts don't go through hello@, so they still arrive.
- **Checking:** send hello@ a test email from another address every fall. There's no key; keep the account's sign-in (with
  two-factor authentication) somewhere both organizers can reach.

### 🐙 GitHub OAuth App (Sign-In)

- **Lives in:** variable `OAUTH_CLIENT_ID` and secret `OAUTH_CLIENT_SECRET` (AppHost parameters `github-client-id` and
  `github-client-secret`). The app itself is under a GitHub account's **Settings → Developer settings → OAuth Apps**, with
  the callback `/signin-github` on the site.
- **Owner:** the CSAdvent GitHub organization (its **Settings → Developer settings → OAuth Apps**), since 2026-10-27, when
  it was transferred from CodingWithCalvin with the repository. Its client ID and secret didn't change.
- **If it fails** (the secret is revoked or the app deleted): nobody can sign in, so contributors can't claim spots or add
  their posts, and organizers can't reach `/manage`. The public pages keep working.
- **What notices:** nothing. The uptime check and the alerts stay green, so sign in to the live site each November.
- **Rotating:** in the OAuth app, **Generate a new client secret** (the old one keeps working until you delete it), set
  `OAUTH_CLIENT_SECRET`, deploy, sign in to check, then delete the old secret.

### ✍️ Anthropic API Key (the Blurb Check)

- **Lives in:** secret `ANTHROPIC_API_KEY` (parameter `anthropic-api-key`). The key belongs to a workspace in the
  [Anthropic Console](https://console.anthropic.com).
- **Organization:** a C# Advent organization of its own in the Anthropic Console, with a spend limit of $25 a month.
- ✏️ **To fill in:** the usage notification below the limit, if one is set.
- **If it fails** (revoked, or the workspace reaches its spend limit): every post is Couldn't check, and posts aren't
  announced automatically until an organizer marks them.
- **What notices:** an email to the organizers once a post couldn't be checked for 30 minutes. Posts are only checked from
  the day before their reveal, so outside the season nothing notices (an author's **Check my post** just says it couldn't
  check).
- **Spend:** set the workspace's monthly limit to cover December (a season costs cents), with a usage notification below
  it. Authors' own checks are capped at 2,000 that go to Claude in 30 days (`Blurbs:SelfChecksPerMonth`, about $1), so they
  can't use up the limit; if that's ever reached, the logs say "Checks on request are paused" and My spots says so, while
  the season's own checks carry on.
- **Rotating:** create a new key in the same workspace, set the secret, deploy, press **Check now** on an entry page (the
  test season works), then revoke the old key.

### 🐘 Mastodon Access Token

- **Lives in:** secret `MASTODON_ACCESS_TOKEN` (parameter `mastodon-access-token`), for `@csadvent@mastodon.social`.
- **Who can sign in:** Calvin only.
- **If it fails:** automatic Mastodon posts fail, each with its error and **Retry now**, and you get an email when one
  stops trying. Copy and paste still works.
- **What notices:** the 8 AM credential check emails the organizers, but only while Mastodon is Automatic.
- **Rotating:** on mastodon.social, **Preferences → Development**, open the application and regenerate its access token
  (or create a new application with the `write:statuses` scope). Set the secret, deploy, and press **Check** on
  `/manage/social`.

### 🦋 Bluesky App Password

- **Lives in:** secret `BLUESKY_APP_PASSWORD` (parameter `bluesky-app-password`), for the `csadvent.christmas` account.
- **Who can sign in:** Calvin only.
- **If it fails, and what notices:** the same as Mastodon.
- **Rotating:** in Bluesky's **Settings → Privacy and security → App passwords**, add a new one. Set the secret, deploy,
  press **Check**, then delete the old app password.

### 📘 Facebook Page ID and Token

- **Lives in:** variable `FACEBOOK_PAGE_ID` and secret `FACEBOOK_PAGE_TOKEN` (parameters `facebook-page-id` and
  `facebook-page-token`). The token comes from the Meta app on developers.facebook.com.
- **Accounts:** Calvin's Facebook account has the Page role and owns the Meta app.
- **If it fails:** automatic Facebook posts fail. A Facebook password change, losing the Page role, or Meta limiting the app
  all stop the token working.
- **What notices:** the 8 AM credential check, while Facebook is Automatic.
- **Rotating:** make a new Page token as [the deployment guide's Facebook section](deployment.md#facebook) describes, set `FACEBOOK_PAGE_TOKEN`, deploy, and press
  **Check**.

### ✖️ X Credentials

- **Lives in:** secrets `X_API_KEY`, `X_API_KEY_SECRET`, `X_ACCESS_TOKEN` and `X_ACCESS_TOKEN_SECRET` (parameters
  `x-api-key`, `x-api-key-secret`, `x-access-token` and `x-access-token-secret`). All four are needed, or X stays copy and
  paste. They come from the app on [console.x.com](https://console.x.com), owned by `@csadvent`.
- **Who can sign in:** Calvin and Matt. The X developer account has Calvin's card on file and a monthly spending limit; the
  console shows the limit and the credit left.
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
  OIDC tokens for this repository's `production` environment through its federated credential
  `github-production-csadvent-ids`, whose subject is
  `repo:CSAdvent@348828352/csadvent.christmas@1427380667:environment:production`.
- **Owners and billing:** Calvin is the subscription's only Owner (besides this identity), and pays for it.
- **If it fails:** nothing deploys. It breaks if the identity, its federated credential or its role assignment is deleted,
  or if the repository is re-created, renamed or transferred, because the subject names its owner and its name, each with
  its ID. Making it public doesn't matter.
- **Moving or renaming the repository** (it moved from CodingWithCalvin/CSAdvent on 2026-10-27): first add a federated
  credential for the new subject, next to the old one (`az identity federated-credential create`, as in the deployment
  guide's step 1). GitHub writes it with the IDs (`repo:{owner}@{ownerId}/{name}@{repoId}:…`) while the repository's OIDC
  setting says so (`gh api repos/{owner}/{name}/actions/oidc/customization/sub`), and otherwise without
  (`repo:{owner}/{name}:…`), so add both if unsure. Move it, run the Deploy workflow by hand, then delete the credentials
  that didn't sign in.
- **What notices:** the Deploy workflow fails at "Sign in to Azure".
- **Rotating:** nothing to rotate. To rebuild it, follow step 1 of [the deployment guide's setup](deployment.md).

### ✉️ Email (Azure Communication Services)

- **Lives in:** no key. The site's managed identity (`web-identity`) has the Communication and Email Service Owner role on
  the Communication Service `csadvent-acs`, so its `email` connection string is just the endpoint.
- **If it fails:** no email goes out. That happens if the role assignment is removed or the sending domain is
  disconnected. Emails wait in the queue meanwhile, tried again after 1, 5 and 15 minutes, then hourly.
- **What notices:** `/manage/emails/queue` (emails piling up, and failed ones with the service's answer), the error alert
  once the queue gives up on an email (after about 2 hours 20 minutes of tries), and a sample from `/manage/emails`. Once
  it's fixed, **Retry all** on the queue page sends the failed ones.
- **Rotating:** nothing to rotate.

### 🚨 Alert Recipients

- **Lives in:** secret `ALERT_EMAILS` (parameter `alert-emails`): the addresses Azure Monitor's alerts go to, separated by
  commas. Not a credential, but personal, so it's a secret.
- **If it's empty:** the deploy leaves the alerts out. Rules already in Azure stay as they were.
- **Changing it:** set the secret and deploy. Never edit the action group in the portal, because the next deploy puts it
  back.

### 🔏 Data Protection Key (Key Vault)

- **Lives in:** no secret. The Key Vault the deploy made for the database's connection strings (`postgres-kv`, named
  `postgreskv-…` in `rg-csadvent`) holds the RSA key `data-protection`. The deploy creates it (`infra/dataprotection.bicep`)
  and gives the site's identity (`web-identity`) Key Vault Crypto Service Encryption User on that key only: it can wrap
  and unwrap with it, nothing else.
- **What it's for:** it encrypts the keys in the database's `data_protection_keys` table, which protect sign-in cookies,
  antiforgery tokens and the links in emails (unsubscribing, confirming), so a copy of the database (a restored server, a
  dump, a leaked password) can't be used to sign in as an organizer or make someone's unsubscribe link (#193). Keys from
  before #193 aren't encrypted; After Advent has the one-time step that revokes them.
- **If it fails:** the site won't start. A new revision never takes traffic (the previous one keeps serving), and the
  Deploy workflow fails: at **Deploy**, because Azure only finishes updating the web app once its new revision is ready,
  or else at "Check the new revision is serving". The revision's console logs say "Data protection can't use
  the Key Vault key". Check that the key exists and is enabled, and that the role assignment on it is there. On the very
  first deploy the role can take a minute or two to reach Key Vault; the site keeps trying, and restarts until it works.
- **Never delete or disable it.** Every data protection key made since #193 would become unreadable: everyone signed
  out, and the links in every email sent since broken. A deleted key can be recovered for 90 days:
  `az keyvault key recover --vault-name <postgreskv-…> --name data-protection --subscription …`. That, like anything else
  with the key itself, needs Key Vault Crypto Officer on the vault: being Owner of the subscription isn't enough for Key
  Vault's contents.
- **Rotating:** not needed. If you ever do, `az keyvault key rotate` makes a new version: the site wraps new keys with the
  newest version and unwraps each with the version it names, so never delete the old versions.
- **What notices:** the Deploy workflow, the uptime check, and the error alert.

### ✅ Nothing to Look After

- **Application Insights, the Key Vault and the container registry** are created by the deploy and used through managed
  identities (the Key Vault's data protection key is above).
- **The managed certificates** renew themselves while the app runs.
- **LinkedIn and Reddit** have no credentials: they're always copy and paste.

---

## 📬 Email Sending Limits

**The limits now:** Azure Communication Services lets C# Advent send **30 emails a minute and 100 an hour**, the default
for a custom domain. They're **per subscription**, so everything the site sends shares them:

- claim emails (two per claim: one to the contributor, one to the organizers; then one more when it's approved or
  declined). One person can have 3 requests waiting and make 5 an hour, so nobody can use the limit up alone (#137);
- announcements, past-author invitations and readers' daily posts;
- post link reminders and the last call;
- the daily email;
- backup asks;
- blurb emails;
- the organizers' notices.

**The organizers decided not to ask Azure for more** (#136). Instead, everything the site sends goes through an email queue
(#152) that keeps the whole site under the limits, whichever replica sends:

- **25 a minute and 90 an hour** (`Email:Queue:PerMinute` and `PerHour` in `appsettings.json`), counted from what's really
  been sent, leaving a little headroom.
- **What goes first:** emails people are waiting for (claim emails, approvals and declines, backup asks, blurb emails,
  the last call, and everything to the organizers' inbox), then the authors' daily email and post link reminders, then
  list mail: readers' daily posts, then announcements, then past authors' invitations.
- **List mail only goes while the last hour's emails are under 50** (`ListPerHour`), a minute at a time (1 a minute), so
  whatever happens, there's room in the hour for the emails people are waiting for. In a quiet hour that's 50 list emails;
  in a busy one, fewer.
- **Deadlines:** a day's emails that haven't gone by midnight are dropped rather than sent a day late, a backup ask that
  hasn't gone when the next backup is asked is dropped (and the next backup is asked straight away), and invitations
  stop when sign-ups close. `/manage/emails/queue` shows how many expired in the last day.
- **If Azure still says 429** (something else in the subscription sending, say), the queue stops for as long as Azure
  asks, and nothing is lost.

**What that means in practice:**

- **Sign-up day:** each claim is two emails (the contributor's, and the organizers'), so 40 claims in the first hour are
  80 emails, which nearly fills the hour by themselves. Claim emails go first. The announcement goes a minute at a time
  and stops once the hour's emails reach 50, so it yields to the claims it brings in (at 50 an hour, 200 subscribers take
  about 4 hours, and past authors follow). Beyond about 40 claims in an hour, some claim emails wait for the next hour:
  watch `/manage/requests`, not the inbox.
- **December's 9 AM:** about 50 authors' daily emails and a few reminders go in the first minutes; readers' daily posts
  follow a minute at a time, up to 50 an hour once the morning's burst has aged out of the hour (so N readers take about
  N/50 hours, and any not reached by midnight are dropped).
- **What to watch:** `/manage/emails/queue` shows what's waiting by kind, the last minute's and hour's sends against the
  limits, and anything that failed, with **Retry** and **Discard**.

The current limits are in the portal: the Email Communication Service `csadvent-email` → **Provision domains** →
`mail.csadvent.christmas` shows **Email send quota per minute** and **per hour**.

**If you ever do ask for more**: it's free, reviewed by hand, and can take up to 72 hours, so ask well before November.

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
6. **Once it's granted**, record the new limits here and in CLAUDE.md, and raise `Email:Queue:PerMinute`, `PerHour` and
   `ListPerHour` in `appsettings.json` to match, keeping some headroom under each.

---

## 💸 GitHub Actions Minutes

The repository has been public since October 26, 2026, so Actions minutes on GitHub's standard runners are free (#140),
and there's nothing to watch.

**Still in place from when it was private**, because they keep CI and deploys quick:

- A new push to a pull request cancels the CI run and the commit lint it replaces.
- A pull request that only changes `README.md`, `CLAUDE.md` or `docs/` skips the build and the tests. Its "Build and test"
  check still passes, so it can never hold up a required check.
- A push to `main` that only changes those doesn't run CI or deploy at all. Run the Deploy workflow by hand if you want
  one anyway.
- The Azure publish check only runs on pull requests (on `main`, the deploy does the same), and leaves out the migration
  bundle, which would add about two minutes.
- A deploy whose commit `main` has already moved on from stops after its first step, rather than deploying code the next
  deploy replaces.
- Dependabot groups each week's updates into one pull request per ecosystem, and is paused in December.

**Being public:**

- **Forks can't deploy.** Only `main` can deploy (the `production` environment's rule), so pull requests from forks never
  see the secrets, and GitHub asks before running workflows for first-time contributors.
- **Branch protection** is available. Requiring "Build and test" is safe with the documentation skip above.
- **If it ever goes private again:** the organization is on GitHub's Free plan, with 2,000 Linux minutes a month shared by
  its private repositories, and October 2026's pace (about 180 minutes a day) would run out by mid-month. Set an Actions
  budget first.

---

## 🚨 Alerts

Two things watch the site and email the organizers, and neither depends on the site's own email (which goes through
hello@, so it can't warn anyone about itself):

- **An uptime check outside Azure** asks `https://csadvent.christmas/health` every few minutes. It's set up by hand (below).
- **Azure Monitor alerts**, defined in [`infra/alerts.bicep`](../src/CSAdvent.AppHost/infra/alerts.bicep) and created by the
  deploy once the `ALERT_EMAILS` secret is set ([the deployment guide](deployment.md)). They go to every address in it, from Azure's
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
     usually nothing. One that keeps coming back needs a fix. Each step runs on its own (#147), so a failing blurb check
     never stops the reminders or backup asks, and the blurb check and backup asks go a post at a time: "… failed for
     slot N" is only that spot, and the rest went ahead. `/manage`'s **Background jobs** says which step is failing and
     since when.
  3. "Gave up on … email" means the email queue tried an email 6 times over about 2 hours 20 minutes. It's on
     `/manage/emails/queue` with the email service's answer: fix the cause, then **Retry** (or **Retry all**).

  Exceptions only count when the site logged them. The tracing of outgoing requests and database commands also records
  each failure's exception, with no severity: in Application Insights they're exceptions whose `severityLevel` is empty,
  and they used to trip the alert, such as each old post's site the dead-link check found gone (October 27). Whatever
  made the request decides whether its failure is an error, and logs it if it is.

  A failed database command is logged as an error by EF Core before the site can handle it. The alert leaves out EF Core's
  "Failed executing DbCommand" (`CommandError`), because whatever ran the command reports the failure again, and its
  records of the errors the site expects (#224, #225): a duplicate (SqlState 23505), when two runs or replicas save the
  same thing at once, and a missing table or column (42P01, 42703), when code runs before its migrations. So the manual
  query above shows more than the alert counts: read the error that comes after them.

  **A relation or column "does not exist":** the site's code is ahead of the database. Deploys run the migrations before
  the new version starts (#151), so this only happens when a version started without them: its image was changed by hand
  (`az containerapp update`), or someone deployed some other way. Background jobs log it as a warning ("the database isn't
  migrated to this version") and try again on their next run, but a page that reads the new schema fails, and trips this
  alert. Run the migrations job (`az containerapp job start -n migrations -g rg-csadvent`), and the errors stop once it's
  done.
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
- **More than 30 database connections** (`csadvent-postgres-connections`): B1ms allows 50, and Azure keeps 15 of them.
  Each replica of the site opens at most 8 (`connectionsPerReplica` in `AppHost.cs`, #158), so 3 replicas stay under 25.
  Before that cap, the server ran out on October 24–25 ("remaining connection slots are reserved", SqlState 53300). A
  deploy briefly runs the old and new revisions side by side, so one alert that clears within 15 minutes is fine.
  Otherwise see who's connected:

  ```sql
  select usename, application_name, state, count(*) from pg_stat_activity group by 1, 2, 3 order by 4 desc;
  ```
- **Service Health** (`csadvent-service-health`): Azure reported an outage, planned maintenance, a security notice or
  something to act on, for a service in Central US or a global one (DNS, email, Azure's sign-in). Read it in the portal under
  **Service Health**. Outages are Azure's to fix: tell people on social media if the site is affected. Planned database
  maintenance restarts the server for a few minutes, in its window on Sundays from 08:00 UTC (3 AM Eastern in winter), and
  the notice comes 5 days ahead: if that Sunday matters, **Reschedule** it on the server's **Maintenance** page.
- **The uptime check** says the site is down, or its certificate is about to expire:
  1. Open the site and `/health` yourself.
  2. **Unhealthy**: the site can't reach the database (look at the server in the portal, and the database alerts).
  3. **Degraded** (the keyword monitor): a background job step on the server that answered hasn't succeeded for too long
     (#172): four of its runs and at least 30 minutes, an hour for the reminders and backup asks. The site itself still
     works. See "A background job stopped", under If Something Breaks.
  4. **No answer**: look at the `web` container app's revisions in the portal, and at the last deploy in GitHub Actions.

### 📟 Setting Up the Uptime Check (by Hand, Once)

Use the free tier of [UptimeRobot](https://uptimerobot.com) or [Better Stack](https://betterstack.com/uptime). Both organizers
need to be on it.

1. Add an **HTTP(S) monitor** for `https://csadvent.christmas/health` every 5 minutes. It answers 200 when everything's
   fine and 503 when the database is down, and never redirects.
2. Add a **keyword monitor** for the same address, every 5 minutes, that alerts when the response **contains `Degraded`**:
   a background job has stopped succeeding (#172). Degraded still answers 200, on purpose (Unhealthy would take the site
   out of service), so the HTTP monitor can't see it. The answer is only ever `Healthy`, `Degraded` or `Unhealthy`.
3. Turn on the **certificate expiry** reminder if the plan has one, and the domain expiry one too.
4. Alert **both organizers** by email and by the service's phone app.
5. **Set up:** UptimeRobot's free tier, on Calvin's account, alerting Calvin. **To do:** the keyword monitor (step 2).

### 💰 A Budget Alert (Optional, by Hand)

Budgets are set on the subscription, outside the deploy. In the portal: **Cost Management → Budgets → Add**, monthly, with
alerts to both organizers at 80% and 100%. It's free, and only warns: nothing is stopped.

- **Set up (October 26, 2026):** `csadvent-monthly`, $75 a month on the C# Advent subscription. It alerts the
  `csadvent-organizers` action group (the `ALERT_EMAILS` addresses) when actual spending passes 80% ($60), and when the
  month's forecast passes $75.

---

## 🧭 If Something Breaks

- **Alerts:** what each one means is under Alerts, above.
- **The site:** every deploy runs the database migrations job, **before it updates the site** (#151), then checks the
  site. If the migrations fail, the site isn't updated and keeps running the previous version (A Deploy Whose Migrations
  Failed, below). A new revision that can't reach the database never takes traffic, because the health probes keep the old
  one serving. `aspire deploy` by hand runs the migrations first too; changing the site's image by hand
  (`az containerapp update`) doesn't, so run the job yourself first if that image needs a newer schema
  (`az containerapp job start -n migrations -g rg-csadvent`).
- **Logs and telemetry:** Application Insights, in the `rg-csadvent` resource group. The deploy sets neither a retention
  nor a daily cap, so logs are kept for the Log Analytics workspace's default of 30 days, and nothing stops ingestion: the
  budget alert (above) is what warns about the cost. SQL statements aren't logged (#157); each one is still a `postgresql`
  dependency.
- **A background job stopped** (reminders, backup asks or social posts that should have gone didn't, or `/health` says
  Degraded): `/manage`'s **Background jobs** lists every step with when it last succeeded and failed, on any server, and
  says **Late** or **Last run failed**. Search the logs for the step's name ("Backup asks failed", say) to see why. Each
  step runs on its own, so the others carry on meanwhile. If a step is late on one server only (`/health` is Degraded now
  and then, and `/manage` looks fine), restart the `web` container app's active revision in the portal.
- **Emails not arriving:** check `/manage/emails/queue` (waiting, the hour's sends, failed), send a sample from
  `/manage/emails`, and look at the Communication Service in Azure.
- **Every post says "Couldn't check":** Claude isn't answering. Each entry's page says why (a rate limit, an expired key,
  or the workspace's spend limit). Posts aren't announced automatically meanwhile, so mark the day's posts as passed (or
  announce them anyway) by hand.
- **A wrong post on the calendar:** fix it at `/manage/entries`. An edited title gets a fresh share card.
- **A bad deploy:** roll it back. See below.
- **Lost or damaged data:** restore the database to a point before it happened. See below.

---

## ⏪ Rolling Back a Deploy

Rolling back the site never rolls back the database: migrations only go forward. Pass `--subscription …` (the C# Advent
subscription, as in Restoring the Database) to every `az` command.

### 🔁 The Site

1. **Redeploy the last good commit** (about 7 minutes). In GitHub, **Actions → Deploy → Run workflow** on `main`, with
   **sha** set to the commit before the bad one (`git log --oneline origin/main` lists them; short SHAs work), or:

   ```bash
   gh workflow run deploy.yml --ref main -f sha=<commit>
   ```

   The workflow refuses a commit that isn't on `main`. It deploys that commit's AppHost, so infrastructure changes roll back
   too, and its migrations job finds nothing new to run. (For a commit from before #151, the workflow runs the job after
   the deploy instead, as deploys did then.)
2. **Or put back the previous image** (a minute, no build), when the bad change is in the site's code and not the AppHost.
   Every deploy pushes its images tagged `aspire-deploy-<UTC time>` (the Deploy run's log says "Setting default deploy tag
   …"). List the revisions with their images, and switch to the one before:

   ```bash
   az containerapp revision list -n web -g rg-csadvent \
     --query "[].{revision:name, created:properties.createdTime, image:properties.template.containers[0].image}" -o table
   az containerapp update -n web -g rg-csadvent --image <the previous image>
   ```

   Only the image changes: the settings and secrets stay as the last deploy left them.

Either way:

- **It lasts until the next deploy of `main`**, which brings the bad change back. Revert it on `main` in a pull request
  (`git revert <commit>`), and let that deploy.
- **The older code runs against the newer schema.** That's safe while migrations only add things (add first, remove in a
  later release). If the bad deploy's migration removed or renamed something the older code reads, the older code fails
  too: fix forward instead, with a new migration.
- **Never roll back past #193** (the deploy that started encrypting the data protection keys) more than two days after
  it, once its encrypted key has taken over: older code can't read that key, so everyone would be signed out and the links
  in emails sent since would stop working. Fix forward instead.

### 🚧 A Deploy Whose Migrations Failed

Every deploy runs the migrations job before it updates the site (#151): the `run-migrations` step in the Deploy run's
**Deploy** step. When the job fails, or hasn't finished after 10 minutes, the Deploy run fails there with what happened
("… ended as Failed", "… didn't finish within 10 minutes"), and nothing after it runs.

- **The site is fine:** it still runs the previous version, which works with whatever the migrations did, because
  migrations only add things. There's nothing to roll back.
- **The database:** a migration that fails part way is undone with its transaction (PostgreSQL's schema changes are
  transactional, unless a migration opts out of the transaction), but migrations before it in the same deploy may already
  be in. The job's logs say which it applied.
- **Why it failed:** the job's logs are in the portal: the `migrations` Container Apps job, **Execution history**, the
  execution the Deploy run named, then its logs.
- **A passing problem** (the database restarting for maintenance, say): run the Deploy workflow again (**Run workflow** on
  `main`). The job runs again and picks up where it stopped.
- **A broken migration:** fix it in a pull request. The one that failed never finished in production, so changing it is
  safe; never change one the job's logs say it applied (A Bad Migration, below). Merging deploys it.
- **Not finished after 10 minutes:** the job may still be running. Watch its execution history; once it has finished, run
  the Deploy workflow again, and the job finds nothing left to do.

### 🧯 A Bad Migration

When a migration damaged or dropped data:

1. **Find when it ran:** the Deploy run's `run-migrations` step, in its **Deploy** step (or the **Run the migrations**
   step, for a commit from before #151), in UTC.
2. **Restore to just before it** into a new server, and look at it (Restoring the Database, steps 1 and 2, below).
3. **Copy the data back** (step 3 there), then fix the schema:
   - **Only what was lost** (usually): the bad migration stays applied in production, so undo its schema change with a new
     migration, merged and deployed like any change. Never delete a migration that has run in production.
   - **The whole database:** production is back to before the bad migration, its history included, so the next migrations
     job would run it again. Before anything else deploys, change that migration on `main` so it's safe (no database has
     it any more), and deploy that.
4. **Delete the restored server, and every copy on your computer** (steps 4 and 5 there).

---

## 🗄️ Restoring the Database

Every season since 2017 lives in one PostgreSQL Flexible Server, `postgres-54yzcow3wctne` in `rg-csadvent` (Central US).
Azure backs it up automatically, and it can go back to **any second in the last 35 days** (Azure's most, raised from 14 in October 2026, #182), up to a few
minutes ago.
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

5. **Delete every copy on your computer** as soon as the data is back: `csadvent.dump`, any `\copy` files, and anything
   else you saved from either server. They hold authors' email addresses and the data protection keys, and the keys made
   before #193 aren't encrypted, so until they're revoked (After Advent) a copy could be used to make an organizer's
   cookie (#193). Empty the recycle bin or trash too.

### 📦 No Logical Backups (Decided 2026-10-26)

A scheduled `pg_dump` to Blob Storage (the optional part of #182) was weighed and left out. Instead, the point-in-time
backups went from 14 days to 35 (done, `BackupRetentionDays` in `AppHost.cs`; backup storage up to the server's 32 GB is
free, and this database is far smaller). They cover any mistake noticed within 35 days, a deleted server can be restored
for 5 days after, and each finished season's calendar is in the repository's archive files (After Advent), so the history
no longer depends on the database. A dump would add a
storage account, a scheduled Container Apps job with an image of its own (the `postgres` image has no way to upload),
credentials to rotate, and a restore that would need trying every year, for two volunteers. It would also keep deleted
email addresses for as long as the dumps are kept, longer than the privacy page's 35 days.

If a copy outside the server is ever wanted (a season's data lost between its December and the January export, say),
the plan is:

1. **Done:** 35-day retention, Azure's most. Keep an eye on the server's **Backup Storage Used** metric; it's free up to
   32 GB.
2. **A copy outside the server:** a daily step in the web app (no new image), claimed once a day
   across replicas, that copies the tables with Npgsql's `COPY … TO STDOUT` into one gzip file and uploads it with the web
   identity (`DefaultAzureCredential`, Storage Blob Data Contributor) to a storage account added with
   `AddAzureStorage("backups")` (publish mode only), with a lifecycle rule deleting blobs after 30 days. Restoring would be
   a new database from the migrations bundle, then `\copy` each table in. Keep that within the privacy page's 35 days (or change
   the page in the same pull request), and validate with `aspire publish --environment PublishCheck` and `az bicep build`.
