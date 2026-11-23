// ValuPro Azure environment (one per stage: dev, qa, prod).
// Deploy: az deployment group create -g <rg> -f infra/main.bicep -p infra/main.<env>.bicepparam
targetScope = 'resourceGroup'

@allowed(['dev', 'qa', 'prod'])
param environmentName string

param location string = resourceGroup().location

@description('Short name used in resource names.')
param appName string = 'valupro'

@description('Entra ID group that administers the SQL server (object id and display name).')
param sqlAdminGroupObjectId string
param sqlAdminGroupName string

@secure()
@minLength(32)
@description('JWT signing key; stored in Key Vault, never in app settings.')
param jwtSigningKey string

@description('Fixed id of the single firm until SaaS sign-up exists.')
param defaultTenantId string = '0195a000-0000-7000-8000-000000000001'

var isProd = environmentName == 'prod'
var suffix = uniqueString(resourceGroup().id, appName, environmentName)
var baseName = '${appName}-${environmentName}'
var tags = { app: appName, environment: environmentName }

// ---------- Monitoring ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${baseName}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: isProd ? 90 : 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${baseName}'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Secrets ----------
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: 'kv-${appName}-${environmentName}-${take(suffix, 6)}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    enablePurgeProtection: isProd ? true : null
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'Jwt--SigningKey'
  properties: { value: jwtSigningKey }
}

// ---------- File storage (photos, documents, PDFs) ----------
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('st${appName}${environmentName}${suffix}', 24)
  location: location
  tags: tags
  sku: { name: isProd ? 'Standard_ZRS' : 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // managed identity only
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: isProd ? 30 : 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: isProd ? 30 : 7 }
    isVersioningEnabled: isProd
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'valupro'
  properties: { publicAccess: 'None' }
}

// ---------- Database ----------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-${baseName}-${take(suffix, 6)}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
    }
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'sqldb-${baseName}'
  location: location
  tags: tags
  // Dev/QA: serverless, pauses when idle. Prod: provisioned General Purpose.
  sku: isProd
    ? { name: 'GP_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
    : { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 1 }
  properties: {
    autoPauseDelay: isProd ? null : 60
    minCapacity: isProd ? null : json('0.5')
    zoneRedundant: false
    requestedBackupStorageRedundancy: isProd ? 'Geo' : 'Local'
  }
}

// ---------- API (App Service) ----------
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${baseName}'
  location: location
  tags: tags
  kind: 'linux'
  sku: isProd ? { name: 'P0v3', tier: 'PremiumV3' } : { name: 'B1', tier: 'Basic' }
  properties: { reserved: true }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${baseName}-api-${take(suffix, 6)}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: isProd
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: isProd ? 'Production' : 'Staging' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'Jwt__SigningKey', value: '@Microsoft.KeyVault(SecretUri=${jwtSecret.properties.secretUri})' }
        { name: 'Storage__ServiceUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'Storage__Container', value: container.name }
        { name: 'Tenancy__DefaultTenantId', value: defaultTenantId }
        { name: 'Database__MigrateOnStartup', value: 'false' }
      ]
      connectionStrings: [
        {
          name: 'Default'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;Encrypt=True;'
        }
      ]
    }
  }
}

// API identity may read Key Vault secrets and read/write blobs. (SQL access is granted by
// the pipeline with CREATE USER ... FROM EXTERNAL PROVIDER, which Bicep cannot do.)
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource apiVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, api.id, keyVaultSecretsUser)
  properties: {
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
  }
}

resource apiBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, api.id, storageBlobDataContributor)
  properties: {
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
  }
}

// ---------- Web (Angular on Static Web Apps, /api forwarded to the App Service) ----------
resource web 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${baseName}'
  location: 'eastasia' // Static Web Apps has a limited region list; content is served globally.
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' } // Standard is required for a linked backend.
  properties: {}
}

resource webBackend 'Microsoft.Web/staticSites/linkedBackends@2024-04-01' = {
  parent: web
  name: 'api'
  properties: {
    backendResourceId: api.id
    region: location
  }
}

output apiName string = api.name
output webName string = web.name
output webHostname string = web.properties.defaultHostname
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output storageAccountName string = storage.name
output keyVaultName string = vault.name
