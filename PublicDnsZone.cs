using Pulumi;
using AzureNative = Pulumi.AzureNative;


class PublicDnsZone
{
    public PublicDnsZone(string dnsZoneName, Input<string> resourceGroupName)
    {
        var dnsZone = new AzureNative.Network.Zone(dnsZoneName, new()
        {
            ZoneName = dnsZoneName,
            ResourceGroupName = resourceGroupName,
            ZoneType = AzureNative.Network.ZoneType.Public,
            Location = "global"
        });

        DnsZoneId = dnsZone.Id;
    }

    [Output] public Output<string> DnsZoneId {get; set; }

}