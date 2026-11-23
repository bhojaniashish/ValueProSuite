using './main.bicep'

param environmentName = 'dev'
param location = 'centralindia'

// Replace with your Entra ID SQL admin group.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'valupro-sql-admins'

// Supplied by the pipeline from a GitHub secret: never commit a real key.
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY', '')
