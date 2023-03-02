using Pulumi;
using Pulumi.AzureAD;
using Pulumi.Random;
using AzureNative = Pulumi.AzureNative;
using Pulumi.AzureNative.Authorization;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Immutable;
using Pulumi.Kubernetes.Core.V1;
using Pulumi.Kubernetes.Helm;
using Pulumi.Kubernetes.Helm.V3;

class AksLandingZone : Stack
{
    public AksLandingZone()
    {
        // Register Feature
        var subscriptionFeatureRegistration = new AzureNative.Features.SubscriptionFeatureRegistration("aksPreview", new()
        {
            FeatureName = "EnablePodIdentityPreview",
            Properties = new AzureNative.Features.Inputs.SubscriptionFeatureRegistrationPropertiesArgs
            {
                Description = "Enable pod identity preview feature",
                ShouldFeatureDisplayInPortal = false,
                State = "Registered",
            },
            ProviderNamespace = "Microsoft.ContainerService",
        });
        // Enable Debugger
        //while (!Debugger.IsAttached)
        //{
        //    Thread.Sleep(100);
        //}
        // Grab some values from the Pulumi stack configuration (or use defaults)
        var projCfg = new Pulumi.Config();
        var configAzureNative = new Pulumi.Config("azure-native");
        var numWorkerNodes = projCfg.GetInt32("numWorkerNodes") ?? 3;
        var k8sVersion = projCfg.Get("kubernetesVersion") ?? "1.24.3";
        var prefixForDns = projCfg.Get("prefixForDns") ?? "pulumi";
        var nodeVmSize = projCfg.Get("nodeVmSize") ?? "Standard_DS2_v2";
        var location = configAzureNative.Require("location");
        var commonArgs = new LandingZoneArgs(Pulumi.Deployment.Instance.StackName, location, "aks");
        var dnsZoneName = projCfg.Get("dnsZoneName") ?? null;

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
        var agwName = $"agw-{commonArgs.Application}-{commonArgs.LocationShort}-{commonArgs.EnvironmentShort}";

        // Instantiate LandingZone Class for Resource Group and Virtual Network
        var landingZone = new LandingZone(resourceGroupName, vnetCidr, vnetName, subnetArr);
        var monitoring = new Monitoring(lawName, managedGrafanaName, mgmtGroupId, landingZone.ResourceGroupName);


        // look for aks subnet by name
        string aksSubnet = string.Empty;
        foreach (var subnet in subnetArr.EnumerateArray())
        {
            if (subnet.GetProperty("name").GetString().Contains("aks"))
            {
                Pulumi.Log.Info($"Subnet {subnet.GetProperty("name").GetString()} will be used for AKS");
                aksSubnet = subnet.GetProperty("name").GetString();
                break;
            }
        }

        var podIdentityProfile = new AzureNative.ContainerService.Inputs.ManagedClusterPodIdentityProfileArgs
        {
            AllowNetworkPluginKubenet = true,
            Enabled = true,
        };


        // Create Identity for Cluster
        var azureHelper = new AzureHelper(configAzureNative.Get("subscriptionId") ?? string.Empty);
        string roleDefinitionManagedIdentityOperatorId = azureHelper.GetRoleByName("Managed Identity Operator");
        var clusterIdentity = new ManagedIdentity(landingZone.ResourceGroupName, "clusterIdentity", roleDefinitionManagedIdentityOperatorId, landingZone.ResourceGroupId);
        
        // Setup DNS Integration using External DNS
        var currentConfig = Output.Create(AzureNative.Authorization.GetClientConfig.InvokeAsync());
        Pulumi.InputMap<string> secretStringData = new Pulumi.InputMap<string>
        {
            {"tenantId", currentConfig.Apply(q=>q.TenantId)},
            {"subscriptionId", currentConfig.Apply(q=>q.SubscriptionId)},
            {"resourceGroup", landingZone.ResourceGroupName},
            {"useManagedIdentityExtension","true"},
        };
        if (null == dnsZoneName) 
        {
            Pulumi.Log.Info("No DNS Zone Name set in Pulumi Config");
        }
        else
        {   
            Pulumi.Log.Info("External DNS will be setup on Azure");
            var dnsZoneId = new PublicDnsZone(dnsZoneName,landingZone.ResourceGroupName);
            string roleDefinitionId = azureHelper.GetRoleByName("DNS Zone Contributor");
            var managedIdentity = new ManagedIdentity(landingZone.ResourceGroupName, "externalDns", roleDefinitionId, dnsZoneId.DnsZoneId);
            podIdentityProfile.UserAssignedIdentities.Add(new AzureNative.ContainerService.Inputs.ManagedClusterPodIdentityArgs 
            {

                Identity = new AzureNative.ContainerService.Inputs.UserAssignedIdentityArgs {
                    ClientId = managedIdentity.ClientId,
                    ObjectId = managedIdentity.PrincipalId,
                    ResourceId = managedIdentity.Id,
                },
                Name = "external-dns",
                BindingSelector = "external-dns",
                Namespace = "default"
            });
            secretStringData.Add("userAssignedIdentityID", managedIdentity.ClientId);
        }


        // Agent Pool 
        var agentPoolProfiles = new AzureNative.ContainerService.Inputs.ManagedClusterAgentPoolProfileArgs
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
        };

        if (!string.IsNullOrEmpty(aksSubnet))
        {
            var subnetId = landingZone.SubnetDictionary.Apply(subnetId => subnetId[aksSubnet]);
            agentPoolProfiles.VnetSubnetID = subnetId;
        }

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
                {
                    "ingress", new AzureNative.ContainerService.Inputs.ManagedClusterAddonProfileArgs
                    {
                        Config = 
                        {
                            { "", "agw_id" },
                        }
                    }
                }
            },
            // Use multiple agent/node pool profiles to distribute nodes across subnets
            AgentPoolProfiles = agentPoolProfiles,

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
                Type = AzureNative.ContainerService.ResourceIdentityType.UserAssigned,
                UserAssignedIdentities = clusterIdentity.Id.Apply(id =>
                {
                    var im = new Dictionary<string, object>
                    {
                        { id, new Dictionary<string, object>() }
                    };
                    return im;
                })
            },
            PodIdentityProfile = podIdentityProfile,
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
        },new CustomResourceOptions{
            DependsOn = subscriptionFeatureRegistration
        });

        // Build a Kubeconfig to access the cluster
        var creds = AzureNative.ContainerService.ListManagedClusterAdminCredentials.Invoke(new()
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

        //// Instantiate Kubernetes Provider
        var k8sProvider = new Pulumi.Kubernetes.Provider("k8s-provider", new Pulumi.Kubernetes.ProviderArgs
        {
           KubeConfig = decoded
        });

        // Setup DNS Integration using External DNS
        if (null == dnsZoneName) 
        {
            Pulumi.Log.Info("No DNS Zone Name set in Pulumi Config");
        }
        else
        {   
            Pulumi.Log.Info("External DNS will be setup on Kubernetes");

            // var ns = new Namespace("externaldns", new Pulumi.Kubernetes.Types.Inputs.Core.V1.NamespaceArgs
            // {
            //     Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs 
            //     {
            //         Name = "externaldns"
            //     }
            // }, new CustomResourceOptions
            // {
            //     Provider = k8sProvider
            // });

            var secret = new Secret("externalDnsSecret", new Pulumi.Kubernetes.Types.Inputs.Core.V1.SecretArgs
            {
                Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs 
                {
                    Name = "azuresecret",
                    // Namespace = Output.Format($"{ns.Metadata.Apply(name => name.Name)}"),
                },
                StringData = secretStringData,
            }, new CustomResourceOptions
            {
                Provider = k8sProvider
            });
        }

        KubeConfig = decoded;
        ClusterName = managedCluster.Name;

    }

    [Output] public Output<string> KubeConfig { get; set; }
    [Output] public Output<string> ClusterName { get; set; }
}