using Pulumi;
using AzureNative = Pulumi.AzureNative;

class ManagedIdentity 
{
    public ManagedIdentity(Input<string> resourceGroupName, string managedIdentityName, string roleDefinitionId, Input<string> roleAssignmentScope ) 
    {
        var identity = new AzureNative.ManagedIdentity.UserAssignedIdentity(managedIdentityName, new ()
        {
            ResourceGroupName = resourceGroupName,
        });

        if (roleDefinitionId != string.Empty)
        {
            var roleAssignmentGuid = new Pulumi.Random.RandomUuid($"guidRoleAssignment{managedIdentityName}");

            var roleAssignment = new AzureNative.Authorization.RoleAssignment($"roleAssignment{managedIdentityName}", new()
            {
                PrincipalId = identity.PrincipalId,
                PrincipalType = "ServicePrincipal",
                RoleAssignmentName = roleAssignmentGuid.Result,
                RoleDefinitionId = roleDefinitionId,
                Scope = roleAssignmentScope,
            });
            RoleAssignmentId = roleAssignment.Id;
        }
        else {
            RoleAssignmentId = Output.Format($"{string.Empty}");
        }

        ClientId = identity.ClientId;
        PrincipalId = identity.PrincipalId;
        Id = identity.Id;
    }

    [Output] public Output<string> ClientId { get; set; }
    [Output] public Output<string> PrincipalId { get; set; }
    [Output] public Output<string> Id { get; set; }
    [Output] public Output<string> RoleAssignmentId { get; set; }
}   