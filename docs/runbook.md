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
| November 1 | Sign-ups open; the announcements list, past authors and the Automatic social platforms hear about it at 9 AM | Automatic, once the season is scheduled; LinkedIn and Reddit by hand from `/manage/social/signups` |
| November | Approve requests, watch the backup list | Organizers |
| Any time | Check requests to add older posts to people's accounts, and for new links to older posts (each one emails the organizers) | Organizers |
| From 7 days before each day | "Your post is due" reminders, then a last call on the day | Automatic |
| December 1–25 | Reveals at midnight, announcements at 10 AM and 2 PM, the daily email at 9 AM | Automatic, plus LinkedIn and Reddit by hand |
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
   1. Press **Check** on Mastodon, Bluesky and Facebook, and **Send a test post** on each (then delete the test posts).
   2. Set each to **Automatic**.

   LinkedIn and Reddit are always copy and paste.
7. **Check email:** send yourself a sample from `/manage/emails`, and send a test email to hello@csadvent.christmas from
   another address (it forwards through ImprovMX).
8. **Keep the stack current:** dependency updates, and a deploy (every merge to `main` deploys).
9. **Check the announcements list** at `/manage/announcements`. The "sign-ups are open" email goes to every confirmed
   subscriber by itself at 9 AM on the day sign-ups open, 40 an hour. Write any other news there: save a draft, send a
   test to the organizers' inbox, then send it.
10. **Post "sign-ups open soon"** from `/manage/social/signups` as the day approaches (a week out, the day before). It counts
    down by itself; post it with **Post now**, or copy it for LinkedIn and Reddit.
11. **Try the blurb check** in the test round: give a test spot a real post with the blurb and one without, move "today" to
    their day, and see each entry's page. It needs the Anthropic API key (the README's deploy setup); without it, posts
    aren't checked and nothing is held.

---

## ✍️ November: Sign-Ups

- **Requests** come in at `/manage/requests`, oldest first. A request doesn't reserve a spot, so approve promptly,
  first come, first served. Approving can put someone in any open spot when theirs has gone. Archive duplicates and
  requests you won't take. Approving emails the author.
- **Entries** at `/manage/entries` show every spot's status. You can fix a name or link, put someone from the backup
  list into an open spot, or release a spot.
- **Backup posters** sign up at `/backups/{year}`, and you can see them at `/manage/backups`. Point people there once
  the calendar fills up.
- **Authors** add their post's title and URL themselves on My spots. Nobody sends them by email.
- **Past authors** get an invitation by themselves at 9 AM on the day sign-ups open, a few at a time alongside the
  announcement. `/manage/announcements` shows how many are going out. Nothing to do.

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
- **Automatic posts** go to Mastodon, Bluesky and Facebook at 10 AM (post 1) and 2 PM (post 2), or as soon as a late
  link arrives that day.
  - Post **LinkedIn and Reddit by hand** from `/manage/social`, then **Mark as posted**.
  - Reddit posts go to r/csadvent only, never r/csharp or r/dotnet.
  - A failed automatic post shows its error and a **Retry now** button.
- **The daily email** goes at 9 AM to everyone with a spot, once the day has posts, leaving out any without the blurb.
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
- **Email:** a sample from `/manage/emails` proves the sending domain is still connected. A new custom domain is
  limited to 30 emails a minute and 100 an hour. If a season needs more, ask Azure support to raise it.
- **Database restore:** practise a point-in-time restore once a year, before December (#90).
- **The blurb check's Anthropic API key** lasts until it's revoked. Check its workspace's spend limit in the Anthropic
  Console, and replace the `ANTHROPIC_API_KEY` secret the same way as the social ones if it's ever rotated.
- **Domains:** the managed certificates for csadvent.christmas and www renew themselves while the app is running.

---

## 🧭 If Something Breaks

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
