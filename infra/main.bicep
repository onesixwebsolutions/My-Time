// DayGrid — Azure infrastructure
// App Service (Linux, .NET 8, code/zip deploy) + Azure Database for PostgreSQL Flexible Server
// + Log Analytics / Application Insights.
//
// Deploy (see docs/DEPLOYMENT.md):
//   az deployment group create -g <rg> -f infra/main.bicep -p @infra/main.parameters.json \
//     -p postgresAdminPassword=<pw> emailPassword=<app-password>

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Region for the PostgreSQL server. Defaults to `location`; override if Burstable SKUs are restricted for your subscription in that region.')
param postgresLocation string = location

@description('Short base name (lowercase letters, digits, hyphens). Used to build globally unique resource names.')
@minLength(3)
@maxLength(30)
param appName string = 'daygrid'

@description('App Service plan SKU. Must be Basic (B1) or higher: Always On is required for the background services and SignalR.')
@allowed([
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'S3'
  'P0v3'
  'P1v3'
  'P2v3'
])
param appServiceSku string = 'B1'

@description('PostgreSQL administrator login name (cannot be azure_superuser, admin, administrator, root, guest, public, or start with pg_).')
param postgresAdminLogin string = 'daygridadmin'

@description('PostgreSQL administrator password. 8-128 chars, 3 of: upper, lower, digit, symbol. Avoid ; and quotes (it is embedded in a connection string).')
@secure()
@minLength(8)
param postgresAdminPassword string

@description('SMTP host for outgoing email (reminders, daily digest).')
param emailHost string = 'smtp.gmail.com'

@description('SMTP port.')
param emailPort int = 587

@description('Use STARTTLS for SMTP.')
param emailUseStartTls bool = true

@description('SMTP username (for Gmail: the full Gmail address).')
param emailUser string

@description('SMTP password (for Gmail: a 16-character App Password, not the account password).')
@secure()
param emailPassword string

@description('Display name used in the From header of outgoing email.')
param emailFromName string = 'DayGrid'

@description('IANA time zone used by the app for scheduling/reminders.')
param timeZone string = 'Asia/Kolkata'

@description('Tags applied to all resources.')
param tags object = {
  app: 'daygrid'
}

// ---------------------------------------------------------------------------
// Names
// ---------------------------------------------------------------------------

var baseName = toLower('${appName}-${uniqueString(resourceGroup().id)}')
var planName = '${baseName}-plan'
var webAppName = baseName
var postgresServerName = '${baseName}-pg'
var databaseName = 'daygrid'
var logAnalyticsName = '${baseName}-logs'
var appInsightsName = '${baseName}-ai'

var skuTier = startsWith(appServiceSku, 'B') ? 'Basic' : (startsWith(appServiceSku, 'S') ? 'Standard' : 'PremiumV3')

// ---------------------------------------------------------------------------
// Monitoring
// ---------------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---------------------------------------------------------------------------
// PostgreSQL Flexible Server
// ---------------------------------------------------------------------------

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: postgresLocation
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    storage: {
      storageSizeGB: 32
      autoGrow: 'Disabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// 0.0.0.0 - 0.0.0.0 is the special "Allow public access from any Azure service" rule.
resource postgresAllowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------
// App Service
// ---------------------------------------------------------------------------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSku
    tier: skuTier
    capacity: 1
  }
  properties: {
    reserved: true // required for Linux
  }
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${databaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SslMode=Require'

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true // SignalR sticky sessions
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      appCommandLine: 'dotnet DayGrid.Api.dll'
      alwaysOn: true
      webSocketsEnabled: true
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'Database__Mode'
          value: 'External'
        }
        {
          name: 'Database__InitializeSchema'
          value: 'true'
        }
        {
          name: 'ConnectionStrings__Default'
          value: connectionString
        }
        {
          name: 'Email__Host'
          value: emailHost
        }
        {
          name: 'Email__Port'
          value: string(emailPort)
        }
        {
          name: 'Email__UseStartTls'
          value: emailUseStartTls ? 'true' : 'false'
        }
        {
          name: 'Email__User'
          value: emailUser
        }
        {
          name: 'Email__Password'
          value: emailPassword
        }
        {
          name: 'Email__FromName'
          value: emailFromName
        }
        {
          name: 'App__TimeZone'
          value: timeZone
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          // Package is prebuilt by CI (dotnet publish) — don't let Oryx try to rebuild it.
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
        {
          name: 'WEBSITE_HEALTHCHECK_MAXPINGFAILURES'
          value: '5'
        }
      ]
    }
  }
  dependsOn: [
    postgresDb
    postgresAllowAzure
  ]
}

// Send App Service console/HTTP logs to Log Analytics.
resource webAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: webApp
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output postgresFqdn string = postgres.properties.fullyQualifiedDomainName
output webAppPrincipalId string = webApp.identity.principalId
