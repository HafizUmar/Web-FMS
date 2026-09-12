/*
  Everything the application needs in Azure, as one deployable file.

  Written so the free tier is the default and raising it later is a parameter rather than
  an edit: a factory that outgrows F1 changes one value, not this file.

  What this creates:
    - a Windows App Service plan and web app, .NET 8, HTTPS only
    - an Azure SQL server and database
    - the app settings the application reads, including the connection string
    - a firewall rule letting Azure services reach the database

  What it deliberately does NOT do:
    - put any password in the template. The SQL password is a secure parameter supplied at
      deployment time, and the application's own bootstrap password likewise.
    - switch on access restrictions. They are written below and inert until an address is
      supplied, which was the decision: reachable now, restricted when the factory's
      address is known.
*/

@description('Base name for every resource. Lower case letters and digits; becomes part of the public hostname.')
@minLength(3)
@maxLength(20)
param appName string

@description('Where to create everything. Defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('App Service plan size. F1 is the free tier: no Always On, no custom-domain TLS, and a 60 CPU-minute daily quota. B1 is the smallest paid tier and removes all three limits.')
@allowed(['F1', 'B1', 'B2', 'S1'])
param appServicePlanSku string = 'F1'

@description('SQL administrator login name.')
param sqlAdminLogin string = 'crockeryadmin'

@description('SQL administrator password. Supplied at deployment; never stored in this file.')
@secure()
@minLength(12)
param sqlAdminPassword string

@description('Use the Azure SQL free offer (a monthly allowance of serverless vCore-seconds). Set false to bill normally.')
param useSqlFreeOffer bool = true

@description('Login name for the first administrator the application creates on an empty database.')
param bootstrapAdminUserName string = 'admin'

@description('Password for that first administrator. Change it from inside the application afterwards, then clear this setting.')
@secure()
@minLength(12)
param bootstrapAdminPassword string

@description('Public IPv4 addresses allowed to reach the site, in CIDR form, e.g. [\'203.0.113.4/32\']. Empty means reachable by anyone, which is the starting position here.')
param allowedIpAddresses array = []

// ---------------------------------------------------------------------------
// Naming
// ---------------------------------------------------------------------------

var sqlServerName = '${appName}-sql'
var sqlDatabaseName = 'CrockeryFactory'
var planName = '${appName}-plan'

// F1 cannot run Always On; asking for it there fails the deployment outright.
var supportsAlwaysOn = appServicePlanSku != 'F1'

// ---------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

/*
  Serverless General Purpose, which is what the free offer applies to. It pauses when
  idle, so an evening or a weekend costs nothing but storage; the price is that the first
  request after a pause waits for the database to wake.
*/
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Local'
    useFreeLimit: useSqlFreeOffer
    freeLimitExhaustionBehavior: useSqlFreeOffer ? 'AutoPause' : null
  }
}

/*
  The 0.0.0.0 rule is Azure's convention for "any Azure service", not "the whole
  internet" - it is how the web app reaches the database without a fixed outbound
  address. The database is not otherwise reachable from outside.
*/
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------
// Application
// ---------------------------------------------------------------------------

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: appServicePlanSku
  }
  properties: {}
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      alwaysOn: supportsAlwaysOn

      // Inert while the list is empty. Supply addresses and every other caller gets a 403.
      ipSecurityRestrictionsDefaultAction: empty(allowedIpAddresses) ? 'Allow' : 'Deny'
      ipSecurityRestrictions: [for (address, index) in allowedIpAddresses: {
        ipAddress: address
        action: 'Allow'
        priority: 100 + index
        name: 'allowed-${index}'
      }]

      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // The pipeline has no separate migration step by choice, so the application
          // applies pending migrations as it starts. It is written not to prevent the
          // host from starting if that fails - see DatabaseMigrator.
          name: 'Database__MigrateOnStartup'
          value: 'true'
        }
        {
          name: 'Bootstrap__AdminUserName'
          value: bootstrapAdminUserName
        }
        {
          // Used once, on a database with no active user. Clear it after the first
          // sign-in and a password change.
          name: 'Bootstrap__AdminPassword'
          value: bootstrapAdminPassword
        }
        {
          name: 'Bootstrap__AdminFullName'
          value: 'Administrator'
        }
      ]

      connectionStrings: [
        {
          name: 'FactoryDatabase'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
      ]
    }
  }
}

output siteUrl string = 'https://${site.properties.defaultHostName}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output webAppName string = site.name
output alwaysOnEnabled bool = supportsAlwaysOn
