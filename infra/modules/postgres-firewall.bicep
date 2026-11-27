// One PostgreSQL firewall rule per address in the list. infra/main.bicep passes its postgresOutboundIpAddresses parameter
// (read before the deploy, never from a resource the same template deploys: that made a dependency circle, see there).
// Used only when postgresFirewallMode = 'containerAppOutboundIps' in infra/main.bicep. UNVERIFIED against a real environment.

@description('Name of the existing PostgreSQL flexible server.')
param serverName string

@description('IPv4 addresses to allow, one rule each.')
param ipAddresses string[]

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: serverName
}

@batchSize(1)
resource rule 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = [
  for ip in ipAddresses: {
    parent: server
    name: 'aca-${replace(ip, '.', '-')}'
    properties: { startIpAddress: ip, endIpAddress: ip }
  }
]
