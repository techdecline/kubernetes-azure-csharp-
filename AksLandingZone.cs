using Pulumi;
using Pulumi.AzureAD;
using Pulumi.Random;
using AzureNative = Pulumi.AzureNative;
using Pulumi.AzureNative.Authorization;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System;

class AksLandingZone : Stack
{
    public AksLandingZone()
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
        var subnetArr = projCfg.RequireObject<JsonElement>("subnets");
        var vnetCidr = projCfg.Require("virtual-network-cidr");

        // Generate Names
        var resourceGroupName = $"rg-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var vnetName = $"vnet-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var clusterName = $"aks-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var lawName = $"law-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";
        var managedGrafanaName = $"grf-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";

        // Instantiate LandingZone Class for Resource Group and Virtual Network
        var landingZone = new LandingZone(resourceGroupName, vnetCidr, vnetName, subnetArr);
        var monitoring = new Monitoring(lawName, managedGrafanaName, mgmtGroupId, landingZone.ResourceGroupName);


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
                    { "logAnalyticsWorkspaceResourceID", monitoring.LogAnalyticsWorkspaceId },
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
                // VnetSubnetID = landingZone.SubnetDictionary["asd"].,
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
            ResourceGroupName = landingZone.ResourceGroupName,
        });

        // Build a Kubeconfig to access the cluster
        var creds = AzureNative.ContainerService.ListManagedClusterUserCredentials.Invoke(new()
        {
            ResourceGroupName = landingZone.ResourceGroupName,
            ResourceName = managedCluster.Name,
        });
        var encoded = creds.Apply(result => result.Kubeconfigs[0]!.Value);
        var decoded = encoded.Apply(enc =>
        {
            var bytes = Convert.FromBase64String(enc);
            return Encoding.UTF8.GetString(bytes);
        });

        var subnetId = landingZone.SubnetDictionary.Apply(obj => obj["aks_subnet"]);
        Pulumi.Log.Info($"AKS Subnet Id: {subnetId}");
        KubeConfig = decoded;
        ClusterName = managedCluster.Name;
        SubnetDictionary = landingZone.SubnetDictionary;
    }

    [Output] public Output<string> KubeConfig { get; set; }
    [Output] public Output<string> ClusterName { get; set; }

    [Output] public Output<ImmutableDictionary<string, object>> SubnetDictionary { get; set; }
}
