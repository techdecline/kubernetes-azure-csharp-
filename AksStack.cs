using Pulumi;
using Pulumi.AzureAD;
using Pulumi.Random;
using AzureNative = Pulumi.AzureNative;
using Pulumi.AzureNative.Authorization;
using System.Collections.Generic;
using System.Text;
using System;

class AksStack : Stack
{
    public AksStack()
    {
        // Grab some values from the Pulumi stack configuration (or use defaults)
        var projCfg = new Pulumi.Config();
        var configAzureNative = new Pulumi.Config("azure-native");
        var numWorkerNodes = projCfg.GetInt32("numWorkerNodes") ?? 3;
        var k8sVersion = projCfg.Get("kubernetesVersion") ?? "1.24.3";
        var prefixForDns = projCfg.Get("prefixForDns") ?? "pulumi";
        var nodeVmSize = projCfg.Get("nodeVmSize") ?? "Standard_DS2_v2";
        var location = configAzureNative.Require("location");
        var commonArgs = new LandingZoneArgs(Pulumi.Deployment.Instance.StackName, location, "aks");

        // The next two configuration values are required (no default can be provided)
        var mgmtGroupId = projCfg.Require("mgmtGroupId");
        var sshPubKey = projCfg.Require("sshPubKey");

        // Generate Names
        var resourceGroupName = $"rg-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var vnetName = $"vnet-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var clusterName = $"aks-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var lawName = $"law-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var managedGrafanaName = $"grf-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";


        // Create a new Azure Resource Group
        var resourceGroup = new AzureNative.Resources.ResourceGroup(resourceGroupName);

        // Create a new Azure Virtual Network
        var virtualNetwork = new AzureNative.Network.VirtualNetwork(vnetName, new()
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
        var workspace = new AzureNative.OperationalInsights.Workspace(lawName, new()
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
            SecurityEnabled = true,
            Members = new[]
            {
            mgmtGroupId,
        },
        });

        // Create Grafana Dashboard

        var userAssignedIdentityGrafana = new AzureNative.ManagedIdentity.UserAssignedIdentity("userAssignedIdentityGrafana", new()
        {
            ResourceGroupName = resourceGroup.Name,
        });
        var grafana = new AzureNative.Dashboard.Grafana(managedGrafanaName, new()
        {
            Identity = new AzureNative.Dashboard.Inputs.ManagedServiceIdentityArgs
            {
                Type = "UserAssigned",
                UserAssignedIdentities = userAssignedIdentityGrafana.Id.Apply(id =>
                {
                    var im = new Dictionary<string, object>
                    {
                        { id, new Dictionary<string, object>() }
                    };
                    return im;
                })
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
            // WorkspaceName = "myWorkspace",
            // WorkspaceName = managedGrafanaName,
        });

        // Create Role Assignment for Grafana Identity
        var roleAssignmentGrafanaIdentityGuid = new Pulumi.Random.RandomUuid("guidRoleAssignmentGrafanaIdentity");


        var roleAssignmentGrafanaIdentity = new AzureNative.Authorization.RoleAssignment("roleAssignmentGrafanaIdentity", new()
        {
            PrincipalId = userAssignedIdentityGrafana.PrincipalId,
            PrincipalType = "ServicePrincipal",
            RoleAssignmentName = roleAssignmentGrafanaIdentityGuid.Result,
            RoleDefinitionId = Output.Format($"/subscriptions/{resourceGroup.Id.Apply(id => id.Split('/')[2])}/providers/Microsoft.Authorization/roleDefinitions/43d0d8ad-25c7-4714-9337-8ba259a9fe05"),
            Scope = Output.Format($"/subscriptions/{resourceGroup.Id.Apply(id => id.Split('/')[2])}"),
        });

        // Create Role Assignment for Grafana Admins
        // Guid roleAssignmentIdGuid = Guid.NewGuid();
        var roleAssignmentGrafanaAdminGuid = new Pulumi.Random.RandomUuid("guidRoleAssignmentGrafanaAdmin");
        // Output<string> subscriptionId = resourceGroup.Id.Apply(id => id.Split('/')[2]);

        var roleAssignmentGrafanaAdmin = new AzureNative.Authorization.RoleAssignment("roleAssignmentGrafanaAdmin", new()
        {
            PrincipalId = grafanaGroup.ObjectId,
            PrincipalType = "Group",
            RoleAssignmentName = roleAssignmentGrafanaAdminGuid.Result,
            RoleDefinitionId = Output.Format($"/subscriptions/{resourceGroup.Id.Apply(id => id.Split('/')[2])}/providers/Microsoft.Authorization/roleDefinitions/22926164-76b3-42b3-bc55-97df8dab3e41"),
            Scope = grafana.Id,
        });

        // Create an Azure Kubernetes Cluster
        var managedCluster = new AzureNative.ContainerService.ManagedCluster(clusterName, new()
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


        KubeConfig = decoded;
        ClusterName = managedCluster.Name;

        // Export some values for use elsewhere
        var aksDictionary = new Dictionary<string, object?>
        {
            ["rgName"] = resourceGroup.Name,
            ["networkName"] = virtualNetwork.Name,
            ["clusterName"] = managedCluster.Name,
            ["kubeconfig"] = decoded,
        };
    }

    [Output] public Output<string> KubeConfig { get; set; }
    [Output] public Output<string> ClusterName { get; set; }
}
