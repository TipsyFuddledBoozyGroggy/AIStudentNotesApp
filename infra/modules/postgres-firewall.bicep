// One PostgreSQL firewall rule per address. A module because the address list (the Container Apps outbound addresses)
// is only known while the deployment runs, and a loop needs its length at the start of the deployment it belongs to.
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
