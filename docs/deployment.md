# ☁️ Deploying C# Advent

How the site gets to Azure, and the one-time setup behind it. What to do each year is in the [runbook](runbook.md).

---

## 🧱 What Gets Deployed

Production runs on:

- **Azure Container Apps** for the site.
- **Azure Database for PostgreSQL**: Flexible Server, PostgreSQL 18, Burstable B2s, with 35-day point-in-time backups. Restoring is in [the runbook](runbook.md).
- **Application Insights**.
- The **Azure DNS** zone for csadvent.christmas.
- **Azure Communication Services** for email.
- **Azure Monitor alerts** that email the organizers when something fails.

All of it is modeled in the AppHost, so one `aspire deploy` creates everything. The keys that protect sign-in cookies and email links are kept in the database, encrypted with a key in **Azure Key Vault** that only the site can use, and the deploy creates that too. The blurb check uses Claude through Anthropic's API, with a key (below).

Email can't be sent until the sending domain is verified, which happens after the nameserver switch below.

## 🤖 Deploying from GitHub Actions

[`deploy.yml`](../.github/workflows/deploy.yml) deploys every push to `main` once CI has passed on it.

- **Skipped:** a push that only changes documentation runs neither CI nor the deploy, and a push already overtaken by newer code on `main` is skipped.
- **By hand:** **Run workflow** in the Actions tab redeploys `main`, or an earlier commit of `main` given its SHA. Rolling back is in [the runbook](runbook.md).
- **What it does:** it signs in to Azure with GitHub's OIDC token, so no Azure password is stored, and runs `aspire deploy`. Then it checks the site, and that the new revision is the one serving.
- **Migrations first:** `aspire deploy` runs the database migrations job and waits for it to succeed before it updates the site (#151), so a failed migration leaves the previous version serving.
- **One at a time:** deploys never overlap.

## 🛠️ One-Time Setup

`<sub>` is the subscription ID.

1. **An identity for the workflow**, trusted only for this repository's `production` environment.
   - It has its own resource group, so deleting `rg-csadvent` never deletes it.
   - It's an **Owner** of the subscription, because `aspire deploy` creates the resource group and role assignments.

   ```bash
   az group create -n rg-csadvent-github -l centralus --subscription <sub>
   az identity create -n id-csadvent-github -g rg-csadvent-github --subscription <sub>
   # GitHub names the repository by its owner and its name, each with its ID, so a re-created repository can't sign in, and a
   # renamed or transferred one needs a credential for its new name first (the runbook's Azure Sign-In for GitHub Actions).
   subject=$(gh api repos/CSAdvent/csadvent.christmas --jq '"repo:\(.owner.login)@\(.owner.id)/\(.name)@\(.id):environment:production"')
   az identity federated-credential create -n github-production --identity-name id-csadvent-github -g rg-csadvent-github --subscription <sub> \
     --issuer https://token.actions.githubusercontent.com --subject "$subject" --audiences api://AzureADTokenExchange
   az role assignment create --assignee-object-id <the identity's principalId> --assignee-principal-type ServicePrincipal --role Owner --scope /subscriptions/<sub>
   ```

2. **A `production` environment** in the repository's settings, limited to the `main` branch, with these **variables**:
   - `AZURE_CLIENT_ID`: the identity's client ID.
   - `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`.
   - `AZURE_LOCATION`: `centralus`.
   - `AZURE_RESOURCE_GROUP`: `rg-csadvent`.
   - `PG_ADMIN_USERNAME`.
   - `OAUTH_CLIENT_ID`: the production GitHub OAuth app's.
3. **Two secrets** in the same environment, `PG_ADMIN_PASSWORD` and `OAUTH_CLIENT_SECRET`.
   - `gh secret set PG_ADMIN_PASSWORD --env production` asks for the value without echoing it.
   - GitHub reserves names starting with `GITHUB_`, hence `OAUTH_`.
4. **Optional: automatic social posting** (#61). Add the credentials for any of these, and that platform can be switched to Automatic on `/manage/social` after the next deploy. Without them it stays copy and paste.
   - Mastodon: secret `MASTODON_ACCESS_TOKEN`, from an application on the account's server with the `write:statuses` scope.
   - Bluesky: secret `BLUESKY_APP_PASSWORD`, an app password for the `csadvent.christmas` account.
   - Facebook: variable `FACEBOOK_PAGE_ID` and secret `FACEBOOK_PAGE_TOKEN`. See [Facebook](#facebook) below.
   - X: secrets `X_API_KEY`, `X_API_KEY_SECRET`, `X_ACCESS_TOKEN` and `X_ACCESS_TOKEN_SECRET`. See [X](#x) below.
5. **Optional: the blurb check** (#128). Add the secret `ANTHROPIC_API_KEY`, an API key from the [Anthropic Console](https://console.anthropic.com).
   - Set a monthly spend limit on its workspace while you're there. A season costs cents.
   - Posts are checked from the next deploy. Without the key they aren't, and nothing is held.
6. **Alerts** (#139). Add the secret `ALERT_EMAILS`: the organizers' email addresses, separated by commas (`gh secret set ALERT_EMAILS --env production`).
   - It's a secret rather than a variable, so the addresses never show in the workflow's log.
   - The next deploy creates an action group that emails them. It alerts on errors in the site's logs; on the database's storage, CPU credits and connections; and on Azure's Service Health ([`infra/alerts.bicep`](../src/CSAdvent.AppHost/infra/alerts.bicep)). Without the secret, no alerts are set up.
   - Change who gets them by changing the secret and deploying, never in the portal.
   - The runbook says what each alert means.
7. **An uptime check outside Azure**, on `https://csadvent.christmas/health`, from UptimeRobot's or Better Stack's free tier.
   - It's set up by hand, as the runbook's Alerts section describes.
   - It still works when Azure or our email doesn't.

### Facebook

`FACEBOOK_PAGE_TOKEN` is a **Page** token that never expires. It has to come from a long-lived user token, which takes a few steps:

1. On developers.facebook.com, create an app with the use case **Manage everything on your Page**, and add the permissions `pages_show_list`, `pages_read_engagement` and `pages_manage_posts`.
2. In the **Graph API Explorer**, pick the app, choose **Get User Access Token**, tick those three permissions, and generate it. In Facebook's dialog, select the C# Advent Page.
3. In the **Access Token Debugger**, paste that token and press **Extend Access Token**. The first token only lasts about an hour.
4. Back in the Graph API Explorer, with the extended token, run `GET me/accounts`. The C# Advent Page's `access_token` is `FACEBOOK_PAGE_TOKEN`, and its numeric `id` is `FACEBOOK_PAGE_ID`.
5. Check the Page token in the Access Token Debugger: **Type: Page**, **Expires: Never**, and all three permissions.
6. Switch the app to **Live** mode (it asks for the privacy policy, `https://csadvent.christmas/privacy`). Posts from an app in Development mode can be visible only to people with a role on it.

Changing the Facebook password, or losing the Page role, invalidates the Page token, so check it on `/manage/social` each November.

Meta can also limit the app until you answer a request for more information in the developer portal.

- **What you see:** posts and checks fail with "Any of the pages_read_engagement, … permission(s) must be granted before impersonating a user's page", and the Access Token Debugger rejects the token with the same message.
- **First:** look in the developer portal for that request. It happened in October 2026.
- **If Check still fails once it's answered:** make a new Page token as above, set `FACEBOOK_PAGE_TOKEN` again, and redeploy (`gh workflow run deploy.yml --ref main`), because secrets only reach the site when it deploys.

### X

X needs all four secrets, or it stays copy and paste.

- **Cost:** X's API is paid per use, from prepaid credits.
  - In October 2026 a post with a link (every announcement) cost $0.20, so a season is about $10–15.
  - The daily credential check costs about a cent while X is Automatic. Set X back to Manual after the season to stop the checks.

1. Sign in at [console.x.com](https://console.x.com) **as `@csadvent`**, because the access token is always for the account that owns the app. Accept the Developer Agreement and Policy, and describe the use: posting C# Advent's own announcements.
2. Buy credits with a card, and set a **spending limit** per billing cycle. X's first-purchase bonus credits expire after 3 months, so buy close to the season.
3. Create a **New App**. Copy its **API Key and Secret** straight away, because they're shown once: `X_API_KEY` and `X_API_KEY_SECRET`.
4. In the app's **User authentication settings**, set:
   - the permissions to **Read and write**;
   - the type to **Automated App / Bot**;
   - `https://csadvent.christmas` wherever it asks for a website or callback URL.
5. Generate the **Access Token and Secret**: `X_ACCESS_TOKEN` and `X_ACCESS_TOKEN_SECRET`. Do it after step 4: tokens made before the switch to Read and write stay read-only, so regenerate them if you changed the permissions later.
6. Add the four secrets (`gh secret set X_API_KEY --env production` and so on), and deploy. Then on `/manage/social`, press **Check** and **Send a test post** (it costs a post), and set X to **Automatic**.

## 🗄️ Migrations

The migrations run before the new revision of the site starts. But the previous revision serves while they run, and a rollback runs older code on the newer schema.

- **Keep each migration safe for the code before it:** add columns and tables first, and only remove them in a later release.
- **No migrations from December 1 to 26:** this is the runbook's schema freeze.

## 🌐 Pointing csadvent.christmas at the Site

This was done once for the current environment, and is only needed again for a new one.

The deploy creates the `csadvent.christmas` zone with every record in it ([`infra/dns.bicep`](../src/CSAdvent.AppHost/infra/dns.bicep)):

- the site's A, `www` and `asuid` records;
- the email verification records (SPF, DKIM, DMARC);
- the MX and SPF records for hello@ forwarding;
- the Bluesky handle's `_atproto` record;
- the CSAdvent GitHub organization's `_gh-CSAdvent-o` verification record.

None of it is live until Hover points at the zone.

Azure only accepts a custom domain once its DNS points at the app, and only issues the free managed certificate after that. So the switch goes in this order, and csadvent.christmas has no HTTPS for a few minutes between steps 2 and 4. `<rg>` is the resource group, `<env>` the Container Apps environment and `web` the app.

1. **Switch the nameservers** at Hover to the zone's four Azure nameservers (`az network dns zone show -n csadvent.christmas -g <rg> --query nameServers -o tsv`). This takes any old site off the domain. Wait until `nslookup -type=NS csadvent.christmas` shows them.
2. **Add the domains** to `CustomDomains` in `src/CSAdvent.AppHost/appsettings.json`, without certificates yet, and deploy. Both domains then work over HTTP only:

   ```json
   "CustomDomains": [
     { "Name": "csadvent.christmas" },
     { "Name": "www.csadvent.christmas" }
   ]
   ```
3. **Create the managed certificates**, then wait until `az containerapp env certificate list -n <env> -g <rg> -o table` shows them as Succeeded:

   ```bash
   az containerapp env certificate create -n <env> -g <rg> --hostname csadvent.christmas --validation-method HTTP --certificate-name csadvent-christmas
   az containerapp env certificate create -n <env> -g <rg> --hostname www.csadvent.christmas --validation-method CNAME --certificate-name www-csadvent-christmas
   ```
4. **Add the certificate names** (`"Certificate": "csadvent-christmas"` and `"Certificate": "www-csadvent-christmas"`) and deploy again. Both domains are now on HTTPS, and www redirects to csadvent.christmas.
5. **Verify the email domain**, then wait until all four show as Verified. The `communication` extension for `az` installs on first use:

   ```bash
   for type in Domain SPF DKIM DKIM2; do
     az communication email domain initiate-verification --email-service-name csadvent-email -g <rg> --domain-name mail.csadvent.christmas --verification-type $type
   done
   az communication email domain show --email-service-name csadvent-email -g <rg> --domain-name mail.csadvent.christmas --query verificationStates
   ```
6. **Deploy again** to connect the verified domain to the Communication Service. `aspire deploy` connects it once all four checks pass, and email is sent from then on.
   - A new domain can send 30 emails a minute and 100 an hour, and the email queue keeps the site under that.
   - The runbook's Email Sending Limits section has the details, and how to ask for more.
7. **Set up hello@** in ImprovMX: add csadvent.christmas and an alias `hello` that forwards to the organizers.

Keep `CustomDomains` and DNS records in the AppHost, not set by hand in the portal: every deploy puts back what the model says.
