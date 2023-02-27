using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Pulumi;

class AzureHelper
{
    ArmClient Client;
    string SubscriptionId;

    public AzureHelper (string subscriptionId) 
    {
        // Azure Authentication
        TokenCredential cred = new DefaultAzureCredential();
        Client = new ArmClient(cred);
        SubscriptionId = subscriptionId;
    }
    
    public string GetRoleByName(string roleDefinitionName)
    {
        // Initialize empty return string
        string roleDefinitionId = string.Empty;

        ResourceIdentifier scopeId = new ResourceIdentifier(string.Format("/{0}", SubscriptionId));
        AuthorizationRoleDefinitionCollection collection = Client.GetAuthorizationRoleDefinitions(scopeId);
        
        // iterate over Role Definitions
        foreach (var page in collection)
        {
            if (page.Data.RoleName == roleDefinitionName)
            {
                // Return Role Definition Id
                return page.Data.Id;
            }
        }

        // return empty response
        return roleDefinitionId;
    }
}