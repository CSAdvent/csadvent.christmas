// Alerts that email the organizers when the site, its background jobs or its database are in trouble (#139). The emails
// come from Azure Monitor, not from the site's own email, so they still arrive when that's what's broken. The runbook says
// what each alert means and what to do.

@description('The deployment\'s region, which the log alert runs in, next to Application Insights.')
param location string

@description('Who gets the alerts: email addresses separated by commas.')
@secure()
param alertEmails string

@description('The Application Insights resource the site sends its logs to.')
param appInsightsId string

@description('The PostgreSQL flexible server.')
param postgresId string

@description('The site\'s region as Service Health names it (AZURE_LOCATION is centralus).')
param regionName string = 'Central US'

var emails = filter(map(split(alertEmails, ','), email => trim(email)), email => !empty(email))

resource organizers 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'csadvent-organizers'
  location: 'global'
  properties: {
    groupShortName: 'CSAdvent'
    enabled: true
    emailReceivers: [
      for (email, i) in emails: {
        name: 'organizer-${i + 1}'
        emailAddress: email
        useCommonAlertSchema: true
      }
    ]
  }
}

// Every Error-level log and exception, except the duplicate inserts the code expects and handles: each reminder, backup
// ask and announcement copy is recorded by inserting a row the unique index refuses when it's already there, and EF Core
// logs every refusal at Error (SaveChangesFailed and CommandError, SqlState 23505). That's dozens an hour in December
// until #157 stops it. A unique violation the code doesn't handle still alerts, through what logs it next: the page's
// unhandled exception, or the background job's failed run.
var errorsQuery = '''
union
    (traces | where severityLevel >= 3),
    (exceptions | where isnull(severityLevel) or severityLevel >= 3)
| extend category = tostring(customDimensions.CategoryName)
| where not((category startswith "Microsoft.EntityFrameworkCore" or category startswith "Npgsql")
    and strcat(message, " ", outerMessage, " ", innermostMessage) has "23505")
'''

// Checked every 15 minutes, the cheapest rate. Stateful (autoMitigate), so it emails once when errors start and once when
// they stop, rather than every 15 minutes while they last.
resource errors 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'csadvent-errors'
  location: location
  properties: {
    displayName: 'C# Advent: errors in the site\'s logs'
    description: 'The site or one of its background jobs logged an error in the last 15 minutes. Open the alert\'s search results in Application Insights to see which.'
    severity: 1
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: errorsQuery
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [organizers.id]
    }
  }
}

// The database runs on Burstable B1ms: 32 GB of storage that doesn't grow by itself (the server goes read-only when it's
// nearly full), CPU credits that run out under sustained load (then it's held to 20% of a core), and 50 connections, some
// of them Azure's own.
var postgresAlerts = [
  {
    name: 'csadvent-postgres-storage'
    description: 'The database server\'s storage is over 80% full. At 95% it turns read-only.'
    metric: 'storage_percent'
    aggregation: 'Average'
    operator: 'GreaterThan'
    threshold: 80
    windowSize: 'PT1H'
    evaluationFrequency: 'PT15M'
  }
  {
    // Burstable servers only: remove this one if the server ever moves to General Purpose.
    name: 'csadvent-postgres-cpu-credits'
    description: 'The database server has fewer than 30 CPU credits left (about half an hour of full speed). With none left it runs at 20% of a core.'
    metric: 'cpu_credits_remaining'
    aggregation: 'Average'
    operator: 'LessThan'
    threshold: 30
    windowSize: 'PT15M'
    evaluationFrequency: 'PT5M'
  }
  {
    name: 'csadvent-postgres-connections'
    description: 'The database server had more than 30 connections. B1ms allows 50, some of them reserved for Azure.'
    metric: 'active_connections'
    aggregation: 'Maximum'
    operator: 'GreaterThan'
    threshold: 30
    windowSize: 'PT15M'
    evaluationFrequency: 'PT5M'
  }
]

resource postgres 'Microsoft.Insights/metricAlerts@2018-03-01' = [
  for alert in postgresAlerts: {
    name: alert.name
    location: 'global'
    properties: {
      description: alert.description
      severity: 2
      enabled: true
      scopes: [postgresId]
      evaluationFrequency: alert.evaluationFrequency
      windowSize: alert.windowSize
      criteria: {
        'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
        allOf: [
          {
            criterionType: 'StaticThresholdCriterion'
            name: alert.metric
            metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
            metricName: alert.metric
            timeAggregation: alert.aggregation
            operator: alert.operator
            threshold: alert.threshold
          }
        ]
      }
      autoMitigate: true
      actions: [
        {
          actionGroupId: organizers.id
        }
      ]
    }
  }
]

// Azure's own outages, planned maintenance and security notices that affect this subscription, in the site's region and
// for global services (DNS, email, sign-in to Azure). The subscription only holds C# Advent, so there's no service filter:
// it covers Container Apps and PostgreSQL along with everything else the site needs, and a misspelled service name can't
// quietly turn it off. Free, like every Service Health alert.
resource serviceHealth 'Microsoft.Insights/activityLogAlerts@2020-10-01' = {
  name: 'csadvent-service-health'
  location: 'global'
  properties: {
    description: 'Azure reported an outage, planned maintenance or a security notice affecting C# Advent\'s subscription.'
    enabled: true
    scopes: [subscription().id]
    condition: {
      allOf: [
        {
          field: 'category'
          equals: 'ServiceHealth'
        }
        {
          anyOf: [
            {
              field: 'properties.incidentType'
              equals: 'Incident'
            }
            {
              field: 'properties.incidentType'
              equals: 'Maintenance'
            }
            {
              field: 'properties.incidentType'
              equals: 'Security'
            }
            {
              field: 'properties.incidentType'
              equals: 'ActionRequired'
            }
          ]
        }
        {
          field: 'properties.impactedServices[*].ImpactedRegions[*].RegionName'
          containsAny: [regionName, 'Global']
        }
      ]
    }
    actions: {
      actionGroups: [
        {
          actionGroupId: organizers.id
        }
      ]
    }
  }
}
