// Azure Communication Services Email, sending from donotreply@mail.<domain>. The web app sends with its managed identity,
// so there's no key to store. Its DNS records are in dns.bicep.

// Aspire passes every module the deployment's region. Everything here is global.
#disable-next-line no-unused-params
param location string

@description('The site\'s domain. Email is sent from its mail subdomain.')
param domainName string

@description('The web app\'s managed identity, which is allowed to send.')
param webPrincipalId string

// The email service, the sending domain and its sender are only ever created, never updated: re-applying the domain makes
// Azure throw away its verification, which disconnects it and stops every email. So a change to their settings here only
// applies to a new environment; change a live one in the portal.
@onlyIfNotExists()
resource emailService 'Microsoft.Communication/emailServices@2025-09-01' = {
  name: 'csadvent-email'
  location: 'global'
  properties: {
    dataLocation: 'United States'
  }
}

@onlyIfNotExists()
resource sendingDomain 'Microsoft.Communication/emailServices/domains@2025-09-01' = {
  parent: emailService
  name: 'mail.${domainName}'
  location: 'global'
  properties: {
    domainManagement: 'CustomerManaged'
    // The privacy page promises we don't track opens or clicks.
    userEngagementTracking: 'Disabled'
  }
}

@onlyIfNotExists()
resource sender 'Microsoft.Communication/emailServices/domains/senderUsernames@2025-09-01' = {
  parent: sendingDomain
  name: 'donotreply'
  properties: {
    username: 'DoNotReply'
    displayName: 'C# Advent'
  }
}

// Azure refuses to connect a sending domain before it's verified, which can only happen once the zone in dns.bicep is
// live. So the first deploy after all four checks pass connects it (see docs/deployment.md).
var verification = sendingDomain.properties.verificationStates
var isVerified = verification.Domain.status == 'Verified' && verification.SPF.status == 'Verified' && verification.DKIM.status == 'Verified' && verification.DKIM2.status == 'Verified'

resource communicationService 'Microsoft.Communication/communicationServices@2025-09-01' = {
  name: 'csadvent-acs'
  location: 'global'
  properties: {
    dataLocation: 'United States'
    linkedDomains: isVerified ? [sendingDomain.id] : []
  }
}

// Communication and Email Service Owner, the built-in role Azure documents for sending with an Entra ID token.
var emailSenderRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '09976791-48a7-449e-bb21-39d1a415f350')

resource webCanSend 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, webPrincipalId, emailSenderRole)
  scope: communicationService
  properties: {
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: emailSenderRole
  }
}

output endpoint string = 'https://${communicationService.properties.hostName}'

@description('Whether the sending domain is connected, so email can be sent.')
output domainConnected bool = isVerified

// The values of the DNS records that verify the sending domain. Their names never change, so dns.bicep has them.
output domainVerification string = sendingDomain.properties.verificationRecords.Domain.value
output spf string = sendingDomain.properties.verificationRecords.SPF.value
output dkim string = sendingDomain.properties.verificationRecords.DKIM.value
output dkim2 string = sendingDomain.properties.verificationRecords.DKIM2.value
