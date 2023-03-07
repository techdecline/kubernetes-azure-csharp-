using System.Text.Json;
using Pulumi;
using System.Threading.Tasks;
class AzureExternalDnsSecret
{
    public Output<string>? tenantId { get; set; }
    public Output<string>? subscriptionId { get; set; }
    public Output<string>? resourceGroup { get; set; }
    public Output<string>? useManagedIdentityExtension {get; set;}
    public Output<string>? userAssignedIdentityID {get; set;} 
}