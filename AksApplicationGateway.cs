using Pulumi;
using AzureNative = Pulumi.AzureNative;

class AksApplicationGateway
{
    public AksApplicationGateway(string ApplicationGatewayName, string PublicIpName, string AksClusterName, Input<string>ResourceGroupName, Input<string>ResourceGroupId, Input<string> AgwSubnetId)
    {
        // Name Generation
        string backendAddressPoolName = $"{AksClusterName}-01-agic-beap-0";
        string frontendPortName = $"{AksClusterName}-01-agic-fe-http-0";
        string tlsFrontendPortName = $"{AksClusterName}-01-agic-fe-https-0";
        string frontendPublicIpConfigurationName = $"{AksClusterName}-01-agic-feip-0";
        string frontendPrivateIpConfigurationName = $"{AksClusterName}-01-agic-feip-1";
        string httpSettingName = $"{AksClusterName}-01-agic-be-htst-0";
        string listenerNamePublic = $"{AksClusterName}-01-agic-httplstn-0";
        string requestRoutingRuleName = $"{AksClusterName}-01-agic-rqrt-0";

        // Fetch RG ID
        var resourceGroup = AzureNative.Resources.GetResourceGroup.Invoke(new AzureNative.Resources.GetResourceGroupInvokeArgs
        {
            ResourceGroupName = ResourceGroupName
        });

        // Agw Params
        var frontendPorts = new InputList<AzureNative.Network.Inputs.ApplicationGatewayFrontendPortArgs>{};
        frontendPorts.Add(new AzureNative.Network.Inputs.ApplicationGatewayFrontendPortArgs {
            Name = frontendPortName,
            Port = 80
        });
        frontendPorts.Add(new AzureNative.Network.Inputs.ApplicationGatewayFrontendPortArgs {
            Name = tlsFrontendPortName,
            Port = 443
        });

        // Public IP 
        var publicIp = new AzureNative.Network.PublicIPAddress(PublicIpName, new AzureNative.Network.PublicIPAddressArgs
        {
            ResourceGroupName = ResourceGroupName,
            Sku = new AzureNative.Network.Inputs.PublicIPAddressSkuArgs
            {
                Name = "Standard"
            },
            PublicIPAllocationMethod = "Static"
        });
        PublicIpId = publicIp.Id;
        var agw = new AzureNative.Network.ApplicationGateway(ApplicationGatewayName,new AzureNative.Network.ApplicationGatewayArgs
        {
            ApplicationGatewayName = ApplicationGatewayName,
            ResourceGroupName = ResourceGroupName,
            Sku = new AzureNative.Network.Inputs.ApplicationGatewaySkuArgs
            {
                Name = "WAF_v2",
                Tier = "WAF_v2",
                Capacity = 3,
            },
            GatewayIPConfigurations = new AzureNative.Network.Inputs.ApplicationGatewayIPConfigurationArgs {
                Name = "appGatewayIpConfig",
                Subnet =new AzureNative.Network.Inputs.SubResourceArgs {
                    Id = AgwSubnetId
                }
            },
            FrontendPorts = new[]
            {
                new AzureNative.Network.Inputs.ApplicationGatewayFrontendPortArgs
                {
                    Name = tlsFrontendPortName,
                    Port = 443,
                },
                new AzureNative.Network.Inputs.ApplicationGatewayFrontendPortArgs
                {
                    Name = frontendPortName,
                    Port = 80,
                },
            },
            FrontendIPConfigurations = new[]
            {
                new AzureNative.Network.Inputs.ApplicationGatewayFrontendIPConfigurationArgs
                {
                    Name = frontendPublicIpConfigurationName,
                    PublicIPAddress = new AzureNative.Network.Inputs.SubResourceArgs
                    {
                        Id = publicIp.Id,
                    },
                },
            },
            BackendAddressPools = new[]
            {
                new AzureNative.Network.Inputs.ApplicationGatewayBackendAddressPoolArgs
                {
                    Name = backendAddressPoolName,
                },
            },
            BackendHttpSettingsCollection = new[]
            {
                new AzureNative.Network.Inputs.ApplicationGatewayBackendHttpSettingsArgs
                {
                    CookieBasedAffinity = "Disabled",
                    Name = "appgwbhs",
                    Port = 80,
                    Protocol = "Http",
                    RequestTimeout = 30,
                },
            },
            HttpListeners = new AzureNative.Network.Inputs.ApplicationGatewayHttpListenerArgs 
            {
                Name = listenerNamePublic,
                Protocol = "Http",
                FrontendIPConfiguration = new AzureNative.Network.Inputs.SubResourceArgs
                {
                    Id = ResourceGroupId.Apply(id => $"{id}/providers/Microsoft.Network/applicationGateways/{ApplicationGatewayName}/frontendIPConfigurations/{frontendPublicIpConfigurationName}")
                },
                FrontendPort = new AzureNative.Network.Inputs.SubResourceArgs
                {
                    Id = ResourceGroupId.Apply(id => $"{id}/providers/Microsoft.Network/applicationGateways/{ApplicationGatewayName}/frontendPorts/{frontendPortName}")
                } 
            },
            RequestRoutingRules = new AzureNative.Network.Inputs.ApplicationGatewayRequestRoutingRuleArgs
            {
                Name = requestRoutingRuleName,
                BackendAddressPool = new AzureNative.Network.Inputs.SubResourceArgs
                {
                    Id = ResourceGroupId.Apply(id => $"{id}/providers/Microsoft.Network/applicationGateways/{ApplicationGatewayName}/backendAddressPools/{backendAddressPoolName}")
                },
                BackendHttpSettings = new AzureNative.Network.Inputs.SubResourceArgs
                {
                    Id = ResourceGroupId.Apply(id => $"{id}/providers/Microsoft.Network/applicationGateways/{ApplicationGatewayName}/backendHttpSettingsCollection/{httpSettingName}")
                },
                HttpListener = new AzureNative.Network.Inputs.SubResourceArgs
                {
                    Id = ResourceGroupId.Apply(id => $"{id}/providers/Microsoft.Network/applicationGateways/{ApplicationGatewayName}/httpListeners/{listenerNamePublic}")
                },
                
            },
            WebApplicationFirewallConfiguration = new AzureNative.Network.Inputs.ApplicationGatewayWebApplicationFirewallConfigurationArgs
            {
                Enabled = true,
                FirewallMode = "Prevention",
                RuleSetType = "OWASP",
                RuleSetVersion = "3.0"
            },
        });

        PublicIpId = publicIp.Id;
        ApplicationGatewayId = agw.Id;
    }

    // [Output] public Output<string> ApplicationGatewayId { get; set; }
    [Output] public Output<string> PublicIpId { get; set; }
    [Output] public Output<string> ApplicationGatewayId { get; set; }
}