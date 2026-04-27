@description('Azure region for workshop deployment resources.')
param location string = resourceGroup().location

@description('Environment name used to derive stable resource names.')
param environmentName string

@description('SKU for Azure Container Registry.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param containerRegistrySku string = 'Basic'

var registryNameBase = 'workshoplab${replace(toLower(environmentName), '-', '')}${uniqueString(subscription().subscriptionId, resourceGroup().id)}'
var containerRegistryName = take(registryNameBase, 50)

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  sku: {
    name: containerRegistrySku
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    dataEndpointEnabled: false
    encryption: {
      status: 'disabled'
    }
    policies: {
      quarantinePolicy: {
        status: 'disabled'
      }
      retentionPolicy: {
        days: 7
        status: 'disabled'
      }
      trustPolicy: {
        type: 'Notary'
        status: 'disabled'
      }
      exportPolicy: {
        status: 'enabled'
      }
      softDeletePolicy: {
        retentionDays: 7
        status: 'disabled'
      }
    }
  }
}

output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_RESOURCE_GROUP_NAME string = resourceGroup().name
output AZURE_LOCATION string = location