using Pulumi;
using Pulumi.AzureAD;
using AzureNative = Pulumi.AzureNative;
using Pulumi.AzureNative.Authorization;
using System.Collections.Generic;
using System.Text;
using System;

return await Pulumi.Deployment.RunAsync(() =>
{
    var subscriptionId = Pulumi.AzureNative.Config.SubscriptionId;

    // Grab some values from the Pulumi stack configuration (or use defaults)
    var projCfg = new Pulumi.Config();
    var numWorkerNodes = projCfg.GetInt32("numWorkerNodes") ?? 3;
    var k8sVersion = projCfg.Get("kubernetesVersion") ?? "1.24.3";
    var prefixForDns = projCfg.Get("prefixForDns") ?? "pulumi";
    var nodeVmSize = projCfg.Get("nodeVmSize") ?? "Standard_DS2_v2";

    // The next two configuration values are required (no default can be provided)
    var mgmtGroupId = projCfg.Require("mgmtGroupId");
    var sshPubKey = projCfg.Require("sshPubKey");

    // Create a new Azure Resource Group
    var resourceGroup = new AzureNative.Resources.ResourceGroup("resourceGroup");

    // Create a new Azure Virtual Network
    var virtualNetwork = new AzureNative.Network.VirtualNetwork("virtualNetwork", new()
    {
        AddressSpace = new AzureNative.Network.Inputs.AddressSpaceArgs
        {
            AddressPrefixes = new[]
            {
                "10.0.0.0/16",
            },
        },
        ResourceGroupName = resourceGroup.Name,
    });

    // Create three subnets in the virtual network
    var subnet1 = new AzureNative.Network.Subnet("subnet1", new()
    {
        AddressPrefix = "10.0.0.0/22",
        ResourceGroupName = resourceGroup.Name,
        VirtualNetworkName = virtualNetwork.Name,
    });

    var subnet2 = new AzureNative.Network.Subnet("subnet2", new()
    {
        AddressPrefix = "10.0.4.0/22",
        ResourceGroupName = resourceGroup.Name,
        VirtualNetworkName = virtualNetwork.Name,
    });

    var subnet3 = new AzureNative.Network.Subnet("subnet3", new()
    {
        AddressPrefix = "10.0.8.0/22",
        ResourceGroupName = resourceGroup.Name,
        VirtualNetworkName = virtualNetwork.Name,
    });

    // Create Log Analytics Workspace
    var workspace = new AzureNative.OperationalInsights.Workspace("workspace", new()
    {
        ResourceGroupName = resourceGroup.Name,
        RetentionInDays = 30,
        Sku = new AzureNative.OperationalInsights.Inputs.WorkspaceSkuArgs
        {
            Name = "PerGB2018",
        }
    });


    // Create Grafana Group
    var current = Pulumi.AzureAD.GetClientConfig.InvokeAsync();
    var grafanaGroup = new Pulumi.AzureAD.Group("Grafana Admin Group", new()
    {
        DisplayName = "Grafana Admin Group",
        Owners = new[]
        {
            mgmtGroupId,
        },
        SecurityEnabled = true,
        Members = new[]
        {
            mgmtGroupId,
        },
    });

    // Create Grafana Dashboard
    var grafana = new AzureNative.Dashboard.Grafana("grafana", new()
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
        ResourceGroupName = resourceGroup.Name,
        Sku = new AzureNative.Dashboard.Inputs.ResourceSkuArgs
        {
            Name = "Standard",
        },
        WorkspaceName = "myWorkspace",
    });

    // Create Role Assignment
    var roleDefinitionId = $"/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/22926164-76b3-42b3-bc55-97df8dab3e412";
    var roleAssignment = new AzureNative.Authorization.RoleAssignment("roleAssignmentGrafanaAdmin", new()
    {
        PrincipalId = grafanaGroup.ObjectId,
        PrincipalType = "Group",
        RoleAssignmentName = "Grafana Admin",
        RoleDefinitionId = roleDefinitionId,
        Scope = "subscriptions/a925f2f7-5c63-4b7b-8799-25a5f97bc3b2/resourceGroups/testrg/providers/Microsoft.DocumentDb/databaseAccounts/test-db-account",
    });

    // Create an Azure Kubernetes Cluster
    var managedCluster = new AzureNative.ContainerService.ManagedCluster("managedCluster", new()
    {
        AadProfile = new AzureNative.ContainerService.Inputs.ManagedClusterAADProfileArgs
        {
            EnableAzureRBAC = true,
            Managed = true,
            AdminGroupObjectIDs = new[]
            {
                mgmtGroupId,
            },
        },
        AddonProfiles =
        {
            { "omsagent", new AzureNative.ContainerService.Inputs.ManagedClusterAddonProfileArgs
            {
                Config =
                {
                    { "logAnalyticsWorkspaceResourceID", workspace.Id },
                },
                Enabled = true,
            } },
        },
        // Use multiple agent/node pool profiles to distribute nodes across subnets
        AgentPoolProfiles = new AzureNative.ContainerService.Inputs.ManagedClusterAgentPoolProfileArgs
        {
            AvailabilityZones = new[]
            {
                "1", "2", "3",
            },
            Count = numWorkerNodes,
            EnableNodePublicIP = false,
            Mode = "System",
            Name = "systempool",
            OsType = "Linux",
            OsDiskSizeGB = 30,
            Type = "VirtualMachineScaleSets",
            VmSize = nodeVmSize,
            // Change next line for additional node pools to distribute across subnets
            // VnetSubnetID = subnet1.Id,
        },

        // Change authorizedIPRanges to limit access to API server
        // Changing enablePrivateCluster requires alternate access to API server (VPN or similar)
        ApiServerAccessProfile = new AzureNative.ContainerService.Inputs.ManagedClusterAPIServerAccessProfileArgs
        {
            AuthorizedIPRanges = new[]
            {
                "0.0.0.0/0",
            },
            EnablePrivateCluster = false,
        },
        DnsPrefix = prefixForDns,
        EnableRBAC = true,
        Identity = new AzureNative.ContainerService.Inputs.ManagedClusterIdentityArgs
        {
            Type = AzureNative.ContainerService.ResourceIdentityType.SystemAssigned,
        },
        KubernetesVersion = k8sVersion,
        LinuxProfile = new AzureNative.ContainerService.Inputs.ContainerServiceLinuxProfileArgs
        {
            AdminUsername = "azureuser",
            Ssh = new AzureNative.ContainerService.Inputs.ContainerServiceSshConfigurationArgs
            {
                PublicKeys = new[]
                {
                    new AzureNative.ContainerService.Inputs.ContainerServiceSshPublicKeyArgs
                    {
                        KeyData = sshPubKey,
                    },
                },
            },
        },
        NetworkProfile = new AzureNative.ContainerService.Inputs.ContainerServiceNetworkProfileArgs
        {
            NetworkPlugin = "azure",
            NetworkPolicy = "azure",
            ServiceCidr = "10.96.0.0/16",
            DnsServiceIP = "10.96.0.10",
        },
        ResourceGroupName = resourceGroup.Name,
    });

    // Build a Kubeconfig to access the cluster
    var creds = AzureNative.ContainerService.ListManagedClusterUserCredentials.Invoke(new()
    {
        ResourceGroupName = resourceGroup.Name,
        ResourceName = managedCluster.Name,
    });
    var encoded = creds.Apply(result => result.Kubeconfigs[0]!.Value);
    var decoded = encoded.Apply(enc =>
    {
        var bytes = Convert.FromBase64String(enc);
        return Encoding.UTF8.GetString(bytes);
    });

    // Export some values for use elsewhere
    return new Dictionary<string, object?>
    {
        ["rgName"] = resourceGroup.Name,
        ["networkName"] = virtualNetwork.Name,
        ["clusterName"] = managedCluster.Name,
        ["kubeconfig"] = decoded,
    };
});
