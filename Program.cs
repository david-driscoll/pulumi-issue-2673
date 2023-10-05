using System;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.KeyVault.Inputs;
using Insights = Pulumi.AzureNative.Insights;
using OperationalInsights = Pulumi.AzureNative.OperationalInsights;
using NativeKeyVault = Pulumi.AzureNative.KeyVault;
using Authorization = Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Web.Inputs;
using Deployment = Pulumi.Deployment;
using Kind = Pulumi.AzureNative.Storage.Kind;
using SkuArgs = Pulumi.AzureNative.Storage.Inputs.SkuArgs;
using SkuName = Pulumi.AzureNative.KeyVault.SkuName;
using App = Pulumi.AzureNative.App;
using AppLogsConfigurationArgs = Pulumi.AzureNative.App.V20221001.Inputs.AppLogsConfigurationArgs;
using Config = Pulumi.Config;
using Web = Pulumi.AzureNative.Web;
using ContainerRegistry = Pulumi.AzureNative.ContainerRegistry;
using LogAnalyticsConfigurationArgs = Pulumi.AzureNative.App.V20221001.Inputs.LogAnalyticsConfigurationArgs;
using StorageAccountArgs = Pulumi.AzureNative.Storage.StorageAccountArgs;
using Network = Pulumi.AzureNative.Network;
using ManagedIdentity = Pulumi.AzureNative.ManagedIdentity;
using ResourceArgs = Pulumi.ResourceArgs;


return await Pulumi.Deployment.RunAsync(() =>
{
    static async Task<ILookup<string, ResourceIdentifier>> GetRoleDefinitions(Output<string> accessToken)
    {
        var token = new AccessToken(await accessToken, DateTimeOffset.Now + TimeSpan.FromHours(1));
        var armClient = new ArmClient(DelegatedTokenCredential.Create((_, _) => token));
        var pages = armClient.GetAuthorizationRoleDefinitions(ResourceIdentifier.Root).GetAllAsync().AsPages();
        return await pages.SelectMany(page => page.Values.ToAsyncEnumerable(), (page, collection) => collection)
            .ToLookupAsync(z => z.Data.RoleName, z => z.Data.Id);
    }

    var roleDefinitions = Output.Create(GetRoleDefinitions(Authorization.GetClientToken.Invoke().Apply(z => z.Token)));

    // Create an Azure Resource Group
    var resourceGroup = new ResourceGroup("resourceGroup");

    var environment = new App.ManagedEnvironment(
        "environment", new()
        {
            ResourceGroupName = resourceGroup.Name,
            ZoneRedundant = false,
            Sku = new EnvironmentSkuPropertiesArgs()
            {
                Name = App.SkuName.Consumption,
            }
        }
    );

    var keyVault = new NativeKeyVault.Vault(
        "keyvault", new NativeKeyVault.VaultArgs
        {
            Properties = new NativeKeyVault.Inputs.VaultPropertiesArgs
            {
                EnabledForDeployment = false,
                EnabledForDiskEncryption = false,
                EnabledForTemplateDeployment = false,
                Sku = new Pulumi.AzureNative.KeyVault.Inputs.SkuArgs
                {
                    Family = "A",
                    Name = SkuName.Standard,
                },
                TenantId = Authorization.GetClientConfig.Invoke().Apply(z => z.TenantId),
                EnableSoftDelete = false, // NOTE: This should be enabled in production.
                EnableRbacAuthorization = true,
            },
            ResourceGroupName = resourceGroup.Name
        }
    );

    _ = new Authorization.RoleAssignment(
        "default_access_policy", new()
        {
            PrincipalId = Authorization.GetClientConfig.Invoke().Apply(z => z.ObjectId),
            PrincipalType = Deployment.Instance.StackName == "dev"
                ? Authorization.PrincipalType.User
                : Authorization.PrincipalType.ServicePrincipal,
            RoleDefinitionId = roleDefinitions.Apply(z => z["Key Vault Secrets Officer"].First().ToString()),
            Scope = keyVault.Id
        }
    );

    var secret = new NativeKeyVault.Secret(
        "secret", new NativeKeyVault.SecretArgs
        {
            VaultName = keyVault.Name,
            ResourceGroupName = resourceGroup.Name,
            Properties = new SecretPropertiesArgs
            {
                Value = "this is a super secret!",
                ContentType = "text/plain",
            },
        }
    );


    var identity = new ManagedIdentity.UserAssignedIdentity(
        "identity", new()
        {
            ResourceGroupName = resourceGroup.Name,
        }
    );

    _ = new Authorization.RoleAssignment(
        "secretSecretReader", new()
        {
            PrincipalId = identity.PrincipalId,
            PrincipalType = Authorization.PrincipalType.ServicePrincipal,
            Scope = keyVault.Id,
            RoleDefinitionId = roleDefinitions.Apply(z => z["Key Vault Secrets User"].First().ToString()),
        }
    );
    _ = new Authorization.RoleAssignment(
        "secretReader", new()
        {
            PrincipalId = identity.PrincipalId,
            PrincipalType = Authorization.PrincipalType.ServicePrincipal,
            Scope = keyVault.Id,
            RoleDefinitionId = roleDefinitions.Apply(z => z["Key Vault Reader"].First().ToString()),
        }
    );

    var adminContainer = new App.ContainerApp(
        "admin", new()
        {
            ResourceGroupName = resourceGroup.Name,
            ManagedEnvironmentId = environment.Id,
            Configuration = new ConfigurationArgs
            {
                Ingress = new IngressArgs
                {
                    External = true,
                    Transport = App.IngressTransportMethod.Auto,
                    TargetPort = 80,
                },
                Secrets = new SecretArgs()
                {
                    Name = "mysecret",
                    Identity = identity.Id,
                    KeyVaultUrl = secret.Properties.Apply(z => z.SecretUri)
                }
            },
            Template = new TemplateArgs
            {
                Containers = new ContainerArgs
                {
                    Name = "app",
                    Image = "nginx",
                    Env = new EnvironmentVarArgs()
                    {
                        Name = "mysecret",
                        SecretRef = "mysecret"
                    }
                },
            },
            Identity = new App.Inputs.ManagedServiceIdentityArgs
            {
                Type = App.ManagedServiceIdentityType.SystemAssigned_UserAssigned,
                UserAssignedIdentities = new[] { identity.Id },
            },
        }
    );

    // Export the primary key of the Storage Account
    return new Dictionary<string, object?>
    {
    };
});
