// The Key Vault key that encrypts the site's data protection keys before they're saved in the database (#193). Those keys
// protect sign-in cookies, antiforgery tokens and the links in emails, so without this, a copy of the database (a restored
// backup, a dump, a leaked password) could make an organizer's cookie or anyone's unsubscribe link. The key lives in the Key
// Vault Aspire made for the database's connection strings, and only the web app's identity can use it: to wrap and unwrap,
// nothing else. The web app won't start without it (DataProtectionKeyCheck).

// Aspire passes every module the deployment's region. The key goes where its vault is.
#disable-next-line no-unused-params
param location string

@description('The Key Vault Aspire made for the database\'s connection strings (postgres-kv).')
param keyVaultName string

@description('The web app\'s managed identity, which wraps and unwraps the data protection keys with this key.')
param webPrincipalId string

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

// Key Vault only ever creates a key here: when it exists, a deploy leaves it as it is (the API is "create if not exists"),
// so its versions and settings are never touched by a deploy. Never delete it: every data protection key made since #193
// would become unreadable, which signs everyone out and breaks the links in emails sent since. A deleted key can be
// recovered for 90 days (the runbook says how).
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: vault
  name: 'data-protection'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
    attributes: {
      enabled: true
    }
  }
}

// Key Vault Crypto Service Encryption User: read the key's public part, and wrap and unwrap with it. On this key only, so
// the web app can't use any other key, or read the vault's secrets this way.
var wrapAndUnwrapRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'e147488a-f6f5-4113-8e2d-b22465e65bf6')

resource webCanWrap 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, webPrincipalId, wrapAndUnwrapRole)
  scope: dataProtectionKey
  properties: {
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: wrapAndUnwrapRole
  }
}

@description('The key\'s versionless URL, so data protection always wraps with its newest version (DataProtection:KeyVaultKeyId).')
output keyUri string = dataProtectionKey.properties.keyUri
