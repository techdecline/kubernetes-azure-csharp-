using Pulumi;
using AzureNative = Pulumi.AzureNative;
using System.Collections.Generic;
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

        List<AzureNative.Network.Subnet> subnetObj = new List<AzureNative.Network.Subnet>();
        foreach (var subnet in subnetConfig.EnumerateArray())
        {
            subnetObj.Add(new AzureNative.Network.Subnet(subnet.GetProperty("name").GetString(), new()
            {
                AddressPrefix = subnet.GetProperty("cidr").GetString(),
                ResourceGroupName = resourceGroup.Name,
                VirtualNetworkName = virtualNetwork.Name,
            }));
        }
        var subnets = new Dictionary<string, int>();

        // Map outputs
        ResourceGroupName = resourceGroup.Name;
        VirtualNetworkId = virtualNetwork.Id;
    }

    [Output] public Output<string> ResourceGroupName { get; set; }
    [Output] public Output<string> VirtualNetworkId { get; set; }
}