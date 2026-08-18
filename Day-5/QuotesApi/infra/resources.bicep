@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}


param quotesApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@secure()
param jwtSigningKey string

@description('Resource ID of an existing Container Apps managed environment to deploy into (subscription is capped at 1 environment per region)')
param containerAppsEnvironmentId string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

// Monitor application with Azure Monitor
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    applicationInsightsName: '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: '${abbrs.portalDashboards}${resourceToken}'
    location: location
    tags: tags
  }
}
// Container registry
module containerRegistry 'br/public:avm/res/container-registry/registry:0.1.1' = {
  name: 'registry'
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    roleAssignments:[
      {
        principalId: quotesApiIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
      }
    ]
  }
}

module quotesApiIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'quotesApiidentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}quotesApi-${resourceToken}'
    location: location
  }
}
module quotesApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'quotesApi-fetch-image'
  params: {
    exists: quotesApiExists
    name: 'quotesapi-dev'
  }
}

module quotesApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesApi'
  params: {
    name: 'quotesapi-dev'
    ingressTargetPort: 8080
    scaleMinReplicas: 1
    scaleMaxReplicas: 10
    secrets: {
      secureList:  [
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
    }
    containers: [
      {
        image: quotesApiFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.5')
          memory: '1.0Gi'
        }
        env: [
          {
            name: 'AppInsights__ConnectionString'
            value: monitoring.outputs.applicationInsightsConnectionString
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: quotesApiIdentity.outputs.clientId
          }
          {
            name: 'PORT'
            value: '8080'
          }
          {
            name: 'Jwt__SigningKey'
            secretRef: 'jwt-signing-key'
          }
        ]
      }
    ]
    managedIdentities:{
      systemAssigned: false
      userAssignedResourceIds: [quotesApiIdentity.outputs.resourceId]
    }
    registries:[
      {
        server: containerRegistry.outputs.loginServer
        identity: quotesApiIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironmentId
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-api' })
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_RESOURCE_QUOTES_API_ID string = quotesApi.outputs.resourceId
