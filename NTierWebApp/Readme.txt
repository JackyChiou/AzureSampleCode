You can deploy a Bicep file from your local machine or one that is stored externally. This section describes deploying a local Bicep file.

If you're deploying to a resource group that doesn't exist, create the resource group. The name of the resource group can only include alphanumeric characters, periods, underscores, hyphens, and parenthesis. It can be up to 90 characters. The name can't end in a period.

az login

az group create --name "rg-lab-candel" --location "Central US"

To deploy a local Bicep file, use the --template-file parameter in the deployment command. The following example also shows how to set a parameter value.

az deployment group create --name NTierWebAppDeployment --resource-group "rg-lab-candel" --template-file .\NTierWebApp.bicep

The deployment can take a few minutes to complete. When it finishes, you see a message that includes the result:

Reference:
https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-cli#deploy-local-bicep-file