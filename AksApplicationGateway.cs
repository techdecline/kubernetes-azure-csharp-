using Pulumi;
using AzureNative = Pulumi.AzureNative;
using AzureClassic = Pulumi.Azure;

class AksApplicationGateway
{
    public AksApplicationGateway(string ApplicationGatewayName, string PublicIpName, string AksClusterName, Input<string>ResourceGroupName, Input<string> AgwSubnetId)
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
        
        var agw = new AzureClassic.Network.ApplicationGateway(ApplicationGatewayName, new()
        {
            ResourceGroupName = ResourceGroupName,
            Sku = new AzureClassic.Network.Inputs.ApplicationGatewaySkuArgs
            {
                Name = "WAF_V2",
                Tier = "WAF_V2",
                Capacity = 2,
            },
            GatewayIpConfigurations = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayGatewayIpConfigurationArgs
                {
                    Name = "my-gateway-ip-configuration",
                    SubnetId = AgwSubnetId,
                },
            },
            FrontendPorts = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayFrontendPortArgs
                {
                    Name = frontendPortName,
                    Port = 80,
                },
            },
            FrontendIpConfigurations = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayFrontendIpConfigurationArgs
                {
                    Name = frontendPublicIpConfigurationName,
                    PublicIpAddressId = PublicIpId,
                },
            },
            BackendAddressPools = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayBackendAddressPoolArgs
                {
                    Name = backendAddressPoolName,
                },
            },
            BackendHttpSettings = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayBackendHttpSettingArgs
                {
                    Name = httpSettingName,
                    CookieBasedAffinity = "Disabled",
                    Path = "/path1/",
                    Port = 80,
                    Protocol = "Http",
                    RequestTimeout = 60,
                },
            },
            HttpListeners = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayHttpListenerArgs
                {
                    Name = listenerNamePublic,
                    FrontendIpConfigurationName = frontendPublicIpConfigurationName,
                    FrontendPortName = frontendPortName,
                    Protocol = "Http",
                },
            },
            RequestRoutingRules = new[]
            {
                new AzureClassic.Network.Inputs.ApplicationGatewayRequestRoutingRuleArgs
                {
                    Name = requestRoutingRuleName,
                    RuleType = "Basic",
                    HttpListenerName = listenerNamePublic,
                    BackendAddressPoolName = backendAddressPoolName,
                    BackendHttpSettingsName = httpSettingName,
                    Priority = 1
                },
            },
            WafConfiguration = new AzureClassic.Network.Inputs.ApplicationGatewayWafConfigurationArgs
            {
                Enabled = true,
                FirewallMode = "Prevention",
                RuleSetType = "OWASP",
                RuleSetVersion = "3.0",
            }
        }, new CustomResourceOptions
        {
            IgnoreChanges = {"sku", "tags"}
        });

        PublicIpId = publicIp.Id;
        ApplicationGatewayId = agw.Id;
    }

    // [Output] public Output<string> ApplicationGatewayId { get; set; }
    [Output] public Output<string> PublicIpId { get; set; }
    [Output] public Output<string> ApplicationGatewayId { get; set; }
}