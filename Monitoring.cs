using Pulumi;
using AzureNative = Pulumi.AzureNative;
using Pulumi.Random;

class Monitoring
{
    public Monitoring(string lawName, string managedGrafanaName, string managedGrafanaAdminGroupId, Input<string> resourceGroupName)
    {
        // Create Log Analytics Workspace
        var workspace = new AzureNative.OperationalInsights.Workspace(lawName, new()
        {
            ResourceGroupName = resourceGroupName,
            RetentionInDays = 30,
            Sku = new AzureNative.OperationalInsights.Inputs.WorkspaceSkuArgs
            {
                Name = "PerGB2018",
            }
        });
        // Create Managed Grafana Dashboard
        var grafana = new AzureNative.Dashboard.Grafana(managedGrafanaName, new()
        {
            Identity = new AzureNative.Dashboard.Inputs.ManagedServiceIdentityArgs
            {
                Type = "SystemAssigned",
            },
            Properties = new AzureNative.Dashboard.Inputs.ManagedGrafanaPropertiesArgs
            {
                ApiKey = "Enabled",
                DeterministicOutboundIP = "Enabled",
                PublicNetworkAccess = "Enabled",
                ZoneRedundancy = "Enabled",
            },
            ResourceGroupName = resourceGroupName,
            Sku = new AzureNative.Dashboard.Inputs.ResourceSkuArgs
            {
                Name = "Standard",
            },
        });

        // Create Role Assignment for Grafana Identity
        var roleAssignmentGrafanaIdentityGuid = new Pulumi.Random.RandomUuid("guidRoleAssignmentGrafanaIdentity");

        var roleAssignmentGrafanaIdentity = new AzureNative.Authorization.RoleAssignment("roleAssignmentGrafanaIdentity", new()
        {
            PrincipalId = grafana.Identity.Apply(opts => opts.PrincipalId),
            PrincipalType = "ServicePrincipal",
            RoleAssignmentName = roleAssignmentGrafanaIdentityGuid.Result,
            RoleDefinitionId = Output.Format($"/subscriptions/{grafana.Id.Apply(id => id.Split('/')[2])}/providers/Microsoft.Authorization/roleDefinitions/43d0d8ad-25c7-4714-9337-8ba259a9fe05"), // Monitoring Reader
            Scope = Output.Format($"/subscriptions/{grafana.Id.Apply(id => id.Split('/')[2])}"),
        });

        // Create Role Assignment for Grafana Admins
        var roleAssignmentGrafanaAdminGuid = new Pulumi.Random.RandomUuid("guidRoleAssignmentGrafanaAdmin");

        var roleAssignmentGrafanaAdmin = new AzureNative.Authorization.RoleAssignment("roleAssignmentGrafanaAdmin", new()
        {
            PrincipalId = managedGrafanaAdminGroupId,
            PrincipalType = "Group",
            RoleAssignmentName = roleAssignmentGrafanaAdminGuid.Result,
            RoleDefinitionId = Output.Format($"/subscriptions/{grafana.Id.Apply(id => id.Split('/')[2])}/providers/Microsoft.Authorization/roleDefinitions/22926164-76b3-42b3-bc55-97df8dab3e41"), // Grafana Admin
            Scope = grafana.Id,
        });

        LogAnalyticsWorkspaceId = workspace.Id;
    }

    [Output] public Output<string> LogAnalyticsWorkspaceId { get; set; }
}