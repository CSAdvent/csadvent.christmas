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

// Every error the site or one of its background jobs logged, except the database libraries' records of errors the code
// expects (#224, #225).
// Exceptions only count when something logged them, with a severity. The tracing of outgoing requests (.NET's own HttpClient
// tracing) and of database commands (Npgsql's) also records each failure's exception on its span, with no severity or
// category, so a site the dead-link check found gone was an alert, though the check expects them and logs them as
// information. Whatever makes the request decides whether its failure is an error, and logs it if it is: in the database
// connection outages of October 24-25, EF Core and the pages logged dozens of errors alongside the spans' records.
// A failed database command is still logged at Error before the code that ran it can handle it, up to twice:
//   - EF Core's CommandError trace (EventId 20102, "Failed executing DbCommand"), whose message is only the SQL. It's left
//     out whatever the error, because the failure is always reported again by what comes after it: EF Core's
//     SaveChangesFailed or failed query, or the code's own error.
//   - EF Core's SaveChangesFailed or failed query exception, left out only for SqlStates the code expects:
//       - 23505, a unique violation: a record another run or replica saved first.
//       - 42P01 and 42703, a missing table or column: new code on an older schema. Deploys run the migrations first (#151),
//         so only a revision started without them. Background jobs log those as warnings and try again on their next run.
// Whatever handles one of these decides whether it's an error: the page's unhandled exception or a background job's failed
// run still alerts. So a page that reads schema the database doesn't have yet alerts too, because a visitor got an error
// page; and a migration that fails is a failed Deploy workflow, with the previous revision still serving.
var errorsQuery = '''
union
    (traces | where severityLevel >= 3),
    (exceptions | where severityLevel >= 3)
| extend category = tostring(customDimensions.CategoryName)
| where not(category == "Microsoft.EntityFrameworkCore.Database.Command" and tostring(customDimensions.EventId) == "20102")
| where not((category startswith "Microsoft.EntityFrameworkCore" or category startswith "Npgsql")
    and strcat(message, " ", outerMessage, " ", innermostMessage) has_any ("23505", "42P01", "42703"))
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

// The database runs on Burstable B2s: 32 GB of storage that doesn't grow by itself (the server goes read-only when it's
// nearly full), CPU credits that run out under sustained load (then it's held to its baseline, a fraction of a core), and
// 429 connections, 15 of them Azure's own. The site should never open more than 49 (connectionsPerReplica in AppHost.cs).
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
    description: 'The database server has fewer than 60 CPU credits left (about half an hour with both cores at full speed). With none left it\'s held to its baseline, a fraction of a core.'
    metric: 'cpu_credits_remaining'
    aggregation: 'Average'
    operator: 'LessThan'
    threshold: 60
    windowSize: 'PT15M'
    evaluationFrequency: 'PT5M'
  }
  {
    name: 'csadvent-postgres-connections'
    description: 'The database server had more than 75 connections: more than the site should ever open (49, during a deploy) and Azure\'s own. B2s allows 414 for us.'
    metric: 'active_connections'
    aggregation: 'Maximum'
    operator: 'GreaterThan'
    threshold: 75
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
