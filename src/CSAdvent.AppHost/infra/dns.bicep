// The site's DNS zone and every record in it. None of it is live until Hover, the registrar, is switched to this zone's
// name servers (see docs/deployment.md).

// Aspire passes every module the deployment's region. DNS zones are global.
#disable-next-line no-unused-params
param location string

@description('The zone, such as csadvent.christmas.')
param domainName string

@description('The Container Apps environment the site runs in.')
param containerAppsEnvironmentName string

@description('The web app\'s name in that environment.')
param webAppName string

@description('The sending domain\'s verification records, from acs.bicep.')
param emailDomainVerification string
param emailSpf string
param emailDkim string
param emailDkim2 string

var ttl = 3600

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: containerAppsEnvironmentName
}

resource zone 'Microsoft.Network/dnsZones@2018-05-01' = {
  name: domainName
  location: 'global'
}

// The site. The apex points at the environment, and www at the web app, which redirects it to the apex. The asuid records
// prove to Container Apps that the domains are ours, before CustomDomains adds them to the app.
resource apex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  parent: zone
  name: '@'
  properties: {
    TTL: ttl
    ARecords: [{ ipv4Address: environment.properties.staticIp }]
  }
}

resource www 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  parent: zone
  name: 'www'
  properties: {
    TTL: ttl
    CNAMERecord: { cname: '${webAppName}.${environment.properties.defaultDomain}' }
  }
}

resource apexVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: 'asuid'
  properties: {
    TTL: ttl
    TXTRecords: [{ value: [environment.properties.customDomainConfiguration.customDomainVerificationId] }]
  }
}

resource wwwVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: 'asuid.www'
  properties: {
    TTL: ttl
    TXTRecords: [{ value: [environment.properties.customDomainConfiguration.customDomainVerificationId] }]
  }
}

// hello@ forwards through ImprovMX. Receiving only: the site sends from the mail subdomain.
resource mx 'Microsoft.Network/dnsZones/MX@2018-05-01' = {
  parent: zone
  name: '@'
  properties: {
    TTL: ttl
    MXRecords: [
      { preference: 10, exchange: 'mx1.improvmx.com' }
      { preference: 20, exchange: 'mx2.improvmx.com' }
    ]
  }
}

resource apexText 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: '@'
  properties: {
    TTL: ttl
    TXTRecords: [{ value: ['v=spf1 include:spf.improvmx.com ~all'] }]
  }
}

// Covers the mail subdomain too. Monitoring only for now, so nothing is rejected.
resource dmarc 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: '_dmarc'
  properties: {
    TTL: ttl
    TXTRecords: [{ value: ['v=DMARC1; p=none'] }]
  }
}

// Keeps csadvent.christmas as the Bluesky account's handle. The DID is the account's, from the old zone.
resource bluesky 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: '_atproto'
  properties: {
    TTL: ttl
    TXTRecords: [{ value: ['did=did:plc:qba2ga6eomasqw4msfke2jbj'] }]
  }
}

// Azure Communication Services: domain ownership and SPF for the sending domain, and its two DKIM keys.
resource mail 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: 'mail'
  properties: {
    TTL: ttl
    TXTRecords: [
      { value: [emailDomainVerification] }
      { value: [emailSpf] }
    ]
  }
}

resource dkim 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  parent: zone
  name: 'selector1-azurecomm-prod-net._domainkey.mail'
  properties: {
    TTL: ttl
    CNAMERecord: { cname: emailDkim }
  }
}

resource dkim2 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  parent: zone
  name: 'selector2-azurecomm-prod-net._domainkey.mail'
  properties: {
    TTL: ttl
    CNAMERecord: { cname: emailDkim2 }
  }
}

@description('The name servers to set at the registrar.')
output nameServers array = zone.properties.nameServers
