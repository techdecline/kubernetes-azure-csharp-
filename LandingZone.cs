using Pulumi;
using AzureNative = Pulumi.AzureNative;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
class LandingZone
{
    public LandingZone(string resourceGroupName, string vnetCidr, string vnetName, JsonElement subnetConfig)
    {
        // Create a new Azure Resource Group
        var resourceGroup = new AzureNative.Resources.ResourceGroup(resourceGroupName);

        // Create a new Azure Virtual Network
        var virtualNetwork = new AzureNative.Network.VirtualNetwork(vnetName, new()
        {
            AddressSpace = new AzureNative.Network.Inputs.AddressSpaceArgs
            {
                AddressPrefixes = new[]
                {
                vnetCidr,
            },
            },
            ResourceGroupName = resourceGroup.Name,
        });
        ImmutableDictionary<string, Output<string>>.Builder outputBuilder = ImmutableDictionary.CreateBuilder<string, Output<string>>();

        foreach (var subnet in subnetConfig.EnumerateArray())
        {
            var subnetObj = new AzureNative.Network.Subnet(subnet.GetProperty("name").GetString(), new()
            {
                AddressPrefix = subnet.GetProperty("cidr").GetString(),
                ResourceGroupName = resourceGroup.Name,
                VirtualNetworkName = virtualNetwork.Name,
            }, new CustomResourceOptions { DeleteBeforeReplace = true });

            outputBuilder.Add(subnet.GetProperty("name").GetString(), subnetObj.Id.Apply(id => id));
        }

        // Map outputs
        ResourceGroupName = resourceGroup.Name;
        ResourceGroupId = resourceGroup.Id;
        VirtualNetworkId = virtualNetwork.Id;
        SubnetDictionary = Output.Create(outputBuilder.ToImmutable());
    }

    [Output] public Output<string> ResourceGroupName { get; set; }
    [Output] public Output<string> ResourceGroupId { get; set; }

    [Output] public Output<string> VirtualNetworkId { get; set; }
    [Output] public Output<ImmutableDictionary<string, Output<string>>> SubnetDictionary { get; set; }
}