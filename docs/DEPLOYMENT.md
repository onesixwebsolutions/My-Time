# Deploying DayGrid to Azure

DayGrid runs as a single-origin app on **Azure App Service (Linux, .NET 8, zip deploy)**: one
`dotnet publish` produces the API, the SignalR hub and the Angular SPA (in `wwwroot`). Data lives in
**Azure Database for PostgreSQL Flexible Server** (v16, Burstable B1ms, 32 GB). Logs and telemetry go
to **Log Analytics + Application Insights**.

| Piece | Where |
|---|---|
| Infrastructure as code | `infra/main.bicep` (example parameters: `infra/main.parameters.example.json`) |
| Infra pipeline (manual) | `.github/workflows/infra.yml`, run with **workflow_dispatch** |
| App pipeline | `.github/workflows/ci-cd.yml`: build and test on every PR and push; deploy on push to `main` or a manual run |
| Dependency updates | `.github/dependabot.yml` (NuGet, npm, GitHub Actions, weekly) |

GitHub authenticates to Azure with **OIDC (workload identity federation)**, so no client secret is
stored anywhere.

---

## 1. One-time setup

You need the Azure CLI, version 2.60 or later. The easiest option is **Azure Cloud Shell (Bash)** at
<https://shell.azure.com>, which already has `az` and is signed in. The commands below use Bash
syntax.

### 1.1 Set variables

```bash
SUBSCRIPTION_ID="<your-subscription-id>"     # az account list -o table
LOCATION="centralindia"                      # or southindia, eastus, ...
RG="rg-daygrid-prod"
GH_REPO="onesixwebsolutions/My-Time"

az account set --subscription "$SUBSCRIPTION_ID"
TENANT_ID=$(az account show --query tenantId -o tsv)
```

### 1.2 Register resource providers (once per subscription)

The deployment identity only gets Contributor on the resource group, so it cannot register providers
itself. Register them once as the subscription owner:

```bash
for ns in Microsoft.Web Microsoft.DBforPostgreSQL Microsoft.OperationalInsights Microsoft.Insights; do
  az provider register --namespace "$ns"
done
# Wait until each one shows "Registered":
az provider list --query "[?contains('Microsoft.Web Microsoft.DBforPostgreSQL Microsoft.OperationalInsights Microsoft.Insights', namespace)].{ns:namespace, state:registrationState}" -o table
```

### 1.3 Create the resource group

```bash
az group create --name "$RG" --location "$LOCATION"
```

### 1.4 Create the identity GitHub Actions will use

Choose **one** option.

#### Option A: Entra ID app registration (service principal)

```bash
APP_ID=$(az ad app create --display-name "github-daygrid-deploy" --query appId -o tsv)
az ad sp create --id "$APP_ID"

# Federated credential for jobs that run in the "production" environment (deploy + infra jobs)
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-env-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:onesixwebsolutions/My-Time:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Federated credential for jobs on the main branch that don't use an environment
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main-branch",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:onesixwebsolutions/My-Time:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Contributor on the resource group only
az role assignment create \
  --assignee "$APP_ID" \
  --role Contributor \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG"

CLIENT_ID="$APP_ID"
```

#### Option B: User-assigned managed identity (no app registration needed)

```bash
az identity create --name "id-github-daygrid" --resource-group "$RG" --location "$LOCATION"
CLIENT_ID=$(az identity show -n id-github-daygrid -g "$RG" --query clientId -o tsv)
PRINCIPAL_ID=$(az identity show -n id-github-daygrid -g "$RG" --query principalId -o tsv)

az identity federated-credential create \
  --name github-env-production \
  --identity-name id-github-daygrid --resource-group "$RG" \
  --issuer "https://token.actions.githubusercontent.com" \
  --subject "repo:onesixwebsolutions/My-Time:environment:production" \
  --audiences "api://AzureADTokenExchange"

az identity federated-credential create \
  --name github-main-branch \
  --identity-name id-github-daygrid --resource-group "$RG" \
  --issuer "https://token.actions.githubusercontent.com" \
  --subject "repo:onesixwebsolutions/My-Time:ref:refs/heads/main" \
  --audiences "api://AzureADTokenExchange"

az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG"
```

> Both workflow jobs that touch Azure declare `environment: production`, so GitHub issues tokens
> with the subject `repo:onesixwebsolutions/My-Time:environment:production`. The `ref:refs/heads/main`
> credential is there for any future job that has no environment.

Print the values you need for GitHub:

```bash
echo "AZURE_CLIENT_ID=$CLIENT_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
echo "AZURE_RESOURCE_GROUP=$RG"
```

### 1.5 Create the GitHub environment

In GitHub, open **Settings → Environments → New environment** and name it `production`. You can add
required reviewers so that every deploy waits for your approval. GitHub would create the environment
automatically on first use, but then it has no protection rules.

### 1.6 Add GitHub secrets and variables

Open **Settings → Secrets and variables → Actions**. Add these as repository-level secrets and
variables, or as `production` environment secrets and variables. Both jobs run in that environment,
so either level works.

**Secrets**

| Name | Value | Used by |
|---|---|---|
| `AZURE_CLIENT_ID` | Client ID of the app registration or managed identity (step 1.4) | both workflows |
| `AZURE_TENANT_ID` | Entra tenant ID | both workflows |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID | both workflows |
| `POSTGRES_ADMIN_PASSWORD` | Strong password: 8–128 chars from at least 3 of upper, lower, digit, symbol. **Do not use `;`, `'` or `"`**, because it is embedded in the Npgsql connection string. | infra.yml |
| `EMAIL_PASSWORD` | SMTP password. For Gmail, an **App Password** (see section 5). | infra.yml |

**Variables**

| Name | Required | Default if unset | Value |
|---|---|---|---|
| `AZURE_RESOURCE_GROUP` | yes | none | e.g. `rg-daygrid-prod` |
| `EMAIL_USER` | yes | none | SMTP user, e.g. `onesixwebsolutions@gmail.com` |
| `AZURE_WEBAPP_NAME` | yes, after the first infra run | none | `webAppName` output of the infra workflow (step 2) |
| `APP_NAME` | no | `daygrid` | Base name for resources (3–30 chars, lowercase/digits/hyphens) |
| `APP_SERVICE_SKU` | no | `B1` | `B1` or higher (Always On needs Basic+) |
| `POSTGRES_ADMIN_LOGIN` | no | `daygridadmin` | Postgres admin user name |
| `EMAIL_FROM_NAME` | no | `DayGrid` | Display name for outgoing mail |
| `APP_TIMEZONE` | no | `Asia/Kolkata` | IANA time zone |

If you use the GitHub CLI, you can set them like this instead:

```bash
gh secret set AZURE_CLIENT_ID       -R "$GH_REPO" -b "$CLIENT_ID"
gh secret set AZURE_TENANT_ID       -R "$GH_REPO" -b "$TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID -R "$GH_REPO" -b "$SUBSCRIPTION_ID"
gh secret set POSTGRES_ADMIN_PASSWORD -R "$GH_REPO"   # prompts for the value
gh secret set EMAIL_PASSWORD          -R "$GH_REPO"   # prompts for the value
gh variable set AZURE_RESOURCE_GROUP -R "$GH_REPO" -b "$RG"
gh variable set EMAIL_USER           -R "$GH_REPO" -b "onesixwebsolutions@gmail.com"
```

---

## 2. Provision the infrastructure

1. In GitHub, open **Actions → Infrastructure → Run workflow**. If you want a preview first, tick
   *what-if*.
2. The first run takes about 10–15 minutes. Most of that time is the PostgreSQL server.
3. The run summary shows `webAppName`, `defaultHostName` and `postgresFqdn`. Set the repository
   variable **`AZURE_WEBAPP_NAME`** to `webAppName`:

   ```bash
   gh variable set AZURE_WEBAPP_NAME -R "$GH_REPO" -b "<webAppName from the summary>"
   # or read it from Azure:
   az deployment group show -g "$RG" -n daygrid-<run-number> --query properties.outputs.webAppName.value -o tsv
   ```

You can re-run the workflow at any time. It is idempotent, so use it to change the SKU or rotate
the DB or email password.

To deploy by hand from Cloud Shell instead of GitHub:

```bash
git clone https://github.com/$GH_REPO.git && cd My-Time
cp infra/main.parameters.example.json infra/main.parameters.json   # edit emailUser etc.
az deployment group create -g "$RG" -f infra/main.bicep -p @infra/main.parameters.json \
  -p postgresAdminPassword='<pw>' emailPassword='<app-password>'
```

### What gets created

| Resource | Name pattern | Notes |
|---|---|---|
| App Service plan | `<appName>-<hash>-plan` | Linux, `B1` |
| Web App | `<appName>-<hash>` | `DOTNETCORE\|8.0`, Always On, WebSockets, ARR affinity, HTTPS only, TLS 1.2, FTPS disabled, health check `/health`, system-assigned identity |
| PostgreSQL Flexible Server | `<appName>-<hash>-pg` | v16, `Standard_B1ms`, 32 GB, 7-day backups, public access + "Allow Azure services" rule |
| Database | `daygrid` | UTF8 |
| Log Analytics | `<appName>-<hash>-logs` | 30-day retention, receives App Service console and HTTP logs |
| Application Insights | `<appName>-<hash>-ai` | `APPLICATIONINSIGHTS_CONNECTION_STRING` is set on the web app |

App settings on the web app: `ASPNETCORE_ENVIRONMENT=Production`, `Database__Mode=External`,
`Database__InitializeSchema=true`, `ConnectionStrings__Default`, `Email__*`, `App__TimeZone`,
`APPLICATIONINSIGHTS_CONNECTION_STRING`, `SCM_DO_BUILD_DURING_DEPLOYMENT=false`.

On first start, the app applies `db/init.sql` to the empty `daygrid` database.

---

## 3. Deploy the app

Push to `main`, or open **Actions → CI/CD → Run workflow**.

- **build-test** runs on every PR and push. It does: `npm ci`, Angular tests (`npm run test:ci`,
  headless Chrome), a Bicep lint, `dotnet build` and `dotnet test` with coverage (TRX and coverage
  files are uploaded as the `test-results` artifact), and `dotnet publish` (which also builds the
  Angular app into `wwwroot`).
- **deploy** runs only for pushes to `main` and manual runs. It does: OIDC login,
  `azure/webapps-deploy` (zip deploy), then a smoke test that polls `https://<host>/health` for
  about 5 minutes. Deploys are serialized with `concurrency: deploy-production`.

---

## 4. Troubleshooting

| Symptom | What to check |
|---|---|
| `azure/login` fails with *AADSTS70021 / no matching federated identity* | The subject must match exactly: `repo:onesixwebsolutions/My-Time:environment:production`. The repo name is case-sensitive. Check that the environment is named `production`. |
| Infra run fails with *AuthorizationFailed* or *MissingSubscriptionRegistration* | Check the Contributor role assignment on the RG (step 1.4) and the provider registration (step 1.2). |
| Postgres create fails with *location is restricted* or *SKU not available* | Some regions don't allow Burstable SKUs for new subscriptions. Pass `postgresLocation=<other region>` to the Bicep (for example `southindia` or `eastus`), or file a quota request. |
| Smoke test fails or the site returns 503 | Stream the logs: `az webapp log tail -g "$RG" -n "$AZURE_WEBAPP_NAME"`. If they're empty, enable them: `az webapp log config -g "$RG" -n "$AZURE_WEBAPP_NAME" --docker-container-logging filesystem`. Also check *Log stream* in the Portal and **Diagnose and solve problems**. |
| `/health` OK but the app errors | Open `https://<host>/health/ready`, which checks DB connectivity. If it fails, confirm the firewall rule *AllowAllAzureServicesAndResourcesWithinAzureIps* exists, the password has no `;`, and `ConnectionStrings__Default` is correct (Portal → Web App → Environment variables). |
| Schema missing / relation does not exist | `Database__InitializeSchema` must be `true`. Restart the app (`az webapp restart -g "$RG" -n "$AZURE_WEBAPP_NAME"`) and watch the logs for the schema bootstrap. |
| App tries to start an embedded Postgres | `Database__Mode` must be `External`. Never use `Embedded` on Azure. |
| SignalR falls back to long polling / disconnects | WebSockets and ARR affinity must be enabled (both set by Bicep). Check `az webapp config show -g "$RG" -n "$AZURE_WEBAPP_NAME" --query "{ws:webSocketsEnabled, alwaysOn:alwaysOn}"`. |
| Reminders or digest emails don't send | Look for SMTP errors in the log stream. Check `Email__User` and `Email__Password` (see section 5). Always On must be true, or background services stop when the app idles. |
| Inspect files / Kudu | `https://<webAppName>.scm.azurewebsites.net` (sign in with your Azure account). |

Run queries in Application Insights (Portal → `<appName>-…-ai` → Logs) or in Log Analytics
(`AppServiceConsoleLogs`, `AppServiceHTTPLogs`).

To connect to the database from your machine, add a temporary firewall rule for your IP:

```bash
az postgres flexible-server firewall-rule create -g "$RG" -n "<postgresServerName>" \
  --rule-name my-ip --start-ip-address <your-ip> --end-ip-address <your-ip>
psql "host=<postgresFqdn> dbname=daygrid user=daygridadmin sslmode=require"
```

---

## 5. Gmail SMTP (App Password)

Gmail rejects your normal account password over SMTP. To get an App Password:

1. Turn on **2-Step Verification** on the Google account.
2. Go to <https://myaccount.google.com/apppasswords>, create an app password named "DayGrid", and
   copy the 16-character code.
3. Store it as the `EMAIL_PASSWORD` secret. Spaces are fine (`abcd efgh ijkl mnop`), and so is the
   code without spaces. Set `EMAIL_USER` to the full Gmail address. Host `smtp.gmail.com`, port `587`
   and STARTTLS are the defaults.
4. Re-run the **Infrastructure** workflow to push the new value to the web app.

Gmail limits sending to roughly 500 recipients per day. For higher volume, use a transactional
provider such as Azure Communication Services Email, SendGrid or Mailgun, and change the `emailHost`,
`emailPort` and credentials accordingly.

---

## 6. Cost (rough estimate)

These are **estimates only**, based on pay-as-you-go list prices. Prices vary by region and change
over time, so check the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/).

| Item | Approx. USD / month |
|---|---|
| App Service plan B1 (Linux) | ~$13 |
| PostgreSQL Flexible B1ms compute | ~$12–15 |
| PostgreSQL storage 32 GB + backups | ~$4 |
| Log Analytics + App Insights (low volume, first 5 GB/month free) | ~$0–3 |
| **Total** | **~$30–35 / month** |

To stop compute charges while you're not using the app: `az postgres flexible-server stop` (it
restarts automatically after 7 days) and `az webapp stop`. The App Service plan is still billed
while the app is stopped. To remove everything: `az group delete -n "$RG"`.

---

## 7. Future hardening (not done yet)

- Move `ConnectionStrings__Default` and `Email__Password` to **Azure Key Vault**, and reference
  them with `@Microsoft.KeyVault(...)` app settings using the web app's system-assigned identity,
  which already exists.
- Replace the Postgres password with **Entra ID authentication** for the managed identity.
- Put Postgres behind **VNet integration / private endpoint** and drop the "Allow Azure services"
  rule. That rule allows *any* Azure tenant's IPs, and only the password protects the server.
- Add a **staging deployment slot** (requires Standard S1+) for zero-downtime swaps.
- Add a custom domain and a managed certificate.
