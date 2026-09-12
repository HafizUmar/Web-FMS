# Deploying to Azure

One Windows App Service, one Azure SQL database, and a GitHub Actions workflow that builds,
tests and deploys on every push.

Everything below is run once. After that, pushing to the branch deploys it.

---

## Before you start

Read this part; it is the part that costs money or surprises you later.

**The free tier is genuinely free, and genuinely limited.**

| | Free (`F1` + SQL free offer) | What you give up |
|---|---|---|
| App Service | £0 | No Always On — the app unloads after ~20 minutes idle, so the first request afterwards takes 10–30 seconds. A **60 CPU-minute daily quota**: exceed it and the site returns 403 until midnight UTC. No custom domain with TLS. No deployment slots. |
| Azure SQL | £0 within the monthly allowance | Serverless, pauses after an hour idle; the first query afterwards waits for it to wake. When the free allowance runs out, the database auto-pauses until the month rolls over. |

For a month's evaluation by a few people this is fine. For a factory whose clerks use it all
day, the 60 CPU-minute quota is the one that will bite — a single busy morning can exhaust
it. Moving to `B1` removes every limit in that column and is the smallest paid tier:

```bash
az deployment group create -g crockery-rg -f infra/main.bicep \
  -p appName=<yourname> appServicePlanSku=B1 ...
```

**The site is reachable by anyone with the URL.** That was the chosen starting position.
This application has no multi-factor authentication and was specified for a trusted factory
LAN, so on the public internet a username and password is the only thing between the
outside world and the factory's books. When you know the factory's public IP address,
restrict it — see [Locking it down](#locking-it-down) at the end. It is one parameter.

---

## 1. Create the Azure resources

You need the Azure CLI: `az login` first.

```bash
# A resource group holds everything, so deleting it deletes everything.
az group create --name crockery-rg --location westeurope

az deployment group create \
  --resource-group crockery-rg \
  --template-file infra/main.bicep \
  --parameters \
      appName=crockeryfactory \
      sqlAdminPassword='<a long password you invent>' \
      bootstrapAdminPassword='<a different long password>'
```

`appName` becomes part of the public hostname, so it must be globally unique and lower
case — `crockeryfactory` is probably taken; use something like `crockeryfactory-lhr`.

When it finishes it prints the site URL, which is `https://<appName>.azurewebsites.net`.

**`bootstrapAdminPassword`** is used exactly once: the application creates the first
administrator with it on a database that has no users, then never again. Note it down.

## 2. Let GitHub deploy without holding a password

GitHub signs in with a short-lived token it requests per run, rather than the repository
storing a standing credential. Three commands:

```bash
# The identity GitHub will act as.
az ad app create --display-name crockery-deploy
APP_ID=$(az ad app list --display-name crockery-deploy --query "[0].appId" -o tsv)
az ad sp create --id "$APP_ID"

# Let it deploy into the resource group, and nothing else in the subscription.
SUB=$(az account show --query id -o tsv)
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/crockery-rg"

# Trust pushes from this repository and branch, and only those.
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-deploy",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:HafizUmar/Web-FMS:ref:refs/heads/claude/funny-noether-g1mh7r",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Add a second federated credential with `...:ref:refs/heads/main` when you start deploying
from `main`. The `subject` must match the branch exactly or the sign-in fails with a
confusing permissions error rather than a naming one.

Also allow the environment the workflow deploys through:

```bash
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-env-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:HafizUmar/Web-FMS:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

## 3. Tell GitHub where to deploy

In the repository, **Settings → Secrets and variables → Actions**.

Secrets:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | the `$APP_ID` printed above |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |

Variables:

| Name | Value |
|---|---|
| `AZURE_WEBAPP_NAME` | the `appName` you used, e.g. `crockeryfactory-lhr` |

Then **Settings → Environments → New environment → `production`**. Leave it empty for now;
it exists so you can later require an approval before a deploy without editing the
workflow.

## 4. Push

```bash
git push
```

Watch it under the **Actions** tab. The workflow builds the Angular client, runs all 212
tests against a real SQL Server, publishes, deploys, and then polls `/api/v1/health` until
the site reports healthy.

Sign in at `https://<appName>.azurewebsites.net` with the bootstrap administrator.

## 5. Immediately after the first sign-in

1. Change that password from inside the application (**account menu → Change password**).
2. Remove the setting that created it, so it is not sitting in the configuration:

```bash
az webapp config appsettings delete -g crockery-rg -n <appName> \
  --setting-names Bootstrap__AdminPassword
```

The bootstrap only runs when no active user exists, so removing it changes nothing about
how the site works. It is simply one fewer password stored where it no longer needs to be.

---

## How a deploy actually goes

```
push ──▶ build job ──────────────────────────▶ deploy job ─────▶ health check
         npm ci, ng build → wwwroot            azure/login        poll /api/v1/health
         dotnet test (real SQL Server)         webapps-deploy     until "Healthy"
         dotnet publish → artifact
```

Two things are worth knowing about the shape of this.

**The Angular build runs before `dotnet publish`, and the workflow fails if it produced
nothing.** `publish` only packages what is on disk, so a silent client-build failure would
otherwise deploy a perfectly working API serving no interface at all — which looks like a
broken deployment rather than a missing step.

**The application applies its own migrations as it starts.** That was a deliberate choice
and it is the weaker of the two options: a pipeline that applies a reviewed script tells
you what is about to change; this tells you afterwards, in a log. It is written so a failed
migration never stops the host from starting — the alternative on App Service is a restart
loop — and `/api/v1/health` reports `migrationsCurrent` either way. To move to the stronger
approach later, add a step before the deploy job:

```yaml
- run: dotnet ef migrations script --idempotent --project src/CrockeryFactory.Web --output migrate.sql
- uses: actions/upload-artifact@v4
  with: { name: migration-script, path: migrate.sql }
- run: sqlcmd -S <server>.database.windows.net -d CrockeryFactory -U <admin> -P <password> -i migrate.sql
```

and set `Database__MigrateOnStartup` to `false`.

---

## Locking it down

When you know the factory's public IP address:

```bash
az deployment group create -g crockery-rg -f infra/main.bicep -p \
  appName=<appName> \
  sqlAdminPassword='<same as before>' \
  bootstrapAdminPassword='<same as before>' \
  allowedIpAddresses='["203.0.113.4/32","198.51.100.7/32"]'
```

Everything not on that list gets a 403 before it reaches the login page. Include your own
address as well as the factory's, or you will lock yourself out along with everyone else.

If the factory's IP changes (most consumer connections do), the site stops working for them
until the list is updated. That is the trade: a static IP from the ISP, or a site that is
reachable by anyone who finds the URL.

---

## Costs, honestly

With the defaults in `infra/main.bicep` and light use, this is free. The two ways it stops
being free:

- **Exceeding the App Service free quota.** 60 CPU-minutes a day. The site returns 403 for
  the rest of the day rather than billing you.
- **Exhausting the SQL free allowance.** The database pauses until the month turns rather
  than billing you.

Neither charges you by surprise; both stop working instead, which for a factory is its own
kind of problem. `appServicePlanSku=B1` and `useSqlFreeOffer=false` is the configuration to
move to when this stops being an evaluation.

To remove everything: `az group delete --name crockery-rg`.
