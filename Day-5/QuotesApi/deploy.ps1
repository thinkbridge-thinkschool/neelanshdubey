<#
.SYNOPSIS
    Azure Container Apps deployment script for QuotesApi.
.DESCRIPTION
    Provisions an Azure Container Registry, Key Vault, and a user-assigned
    managed identity (with RBAC grants for ACR pull + Key Vault secret read),
    then creates the QuotesApi container app wired to all three, with
    external ingress on port 8080 and HTTP-concurrency autoscaling.

    This script never accepts, generates, or embeds secret values. Before
    running it, the following secrets must already exist in the target Key
    Vault (set them once via `az keyvault secret set`, from a shell, never
    committed to source control):
      - JwtSigningKey              (matches Jwt:SigningKey / Jwt__SigningKey)
      - AppInsightsConnectionString (matches AppInsights:ConnectionString)

    Assumes the resource group and Container Apps environment already exist
    (see Day-5 Task 3) and that Docker is available locally to build/push the
    image — `az acr build` (remote build) is not available on subscriptions
    where ACR Tasks are restricted.
.PARAMETER ResourceGroup
    Name of the Azure Resource Group (Default: thinkschool-rg).
.PARAMETER Location
    Azure Region (Default: centralindia).
.PARAMETER EnvironmentName
    Name of the existing Container Apps Environment (Default: thinkschool-env).
.PARAMETER AppName
    Name of the Container App (Default: quotes-api).
.PARAMETER AcrName
    Name of the Azure Container Registry (must be globally unique).
.PARAMETER ImageTag
    Image name:tag to build and deploy (Default: quotes-api:0.1.0).
.PARAMETER KeyVaultName
    Name of the Key Vault holding the app secrets (must be globally unique).
.PARAMETER IdentityName
    Name of the user-assigned managed identity (Default: quotes-api-identity).
#>

[CmdletBinding()]
param (
    [string]$ResourceGroup = "thinkschool-rg",
    [string]$Location = "centralindia",
    [string]$EnvironmentName = "thinkschool-env",
    [string]$AppName = "quotes-api",
    [Parameter(Mandatory = $true)]
    [string]$AcrName,
    [string]$ImageTag = "quotes-api:0.1.0",
    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,
    [string]$IdentityName = "quotes-api-identity"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Azure Container Apps Deployment: QuotesApi ===" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Environment:    $EnvironmentName"
Write-Host "App Name:       $AppName"
Write-Host "ACR:            $AcrName"
Write-Host "Key Vault:      $KeyVaultName"
Write-Host "Identity:       $IdentityName"
Write-Host "------------------------------------------------"

# Step 1: Container Registry
Write-Host "[1/6] Creating Azure Container Registry '$AcrName'..." -ForegroundColor Yellow
az acr create -n $AcrName -g $ResourceGroup -l $Location --sku Basic --admin-enabled false -o table

# Step 2: Build & push the image (local Docker required — ACR Tasks may be
# restricted on trial/limited subscriptions).
Write-Host "[2/6] Building and pushing image..." -ForegroundColor Yellow
$acrLoginServer = az acr show -n $AcrName --query loginServer -o tsv
az acr login -n $AcrName
docker build -t "$acrLoginServer/$ImageTag" .
docker push "$acrLoginServer/$ImageTag"

# Step 3: Key Vault (RBAC-authorized — no access policies)
Write-Host "[3/6] Creating Key Vault '$KeyVaultName'..." -ForegroundColor Yellow
az keyvault create -n $KeyVaultName -g $ResourceGroup -l $Location --enable-rbac-authorization true -o table
Write-Host "NOTE: this script does not populate secrets. Before continuing, run:" -ForegroundColor Yellow
Write-Host "  az keyvault secret set --vault-name $KeyVaultName --name JwtSigningKey --value <value>"
Write-Host "  az keyvault secret set --vault-name $KeyVaultName --name AppInsightsConnectionString --value <value>"
Write-Host "(you may need to grant yourself 'Key Vault Secrets Officer' on the vault first)"

# Step 4: User-assigned managed identity
Write-Host "[4/6] Creating managed identity '$IdentityName'..." -ForegroundColor Yellow
az identity create -n $IdentityName -g $ResourceGroup -l $Location -o table
$identityId = az identity show -n $IdentityName -g $ResourceGroup --query id -o tsv
$principalId = az identity show -n $IdentityName -g $ResourceGroup --query principalId -o tsv

# Step 5: RBAC — let the identity pull from ACR and read Key Vault secrets
Write-Host "[5/6] Granting RBAC roles to the managed identity..." -ForegroundColor Yellow
$acrId = az acr show -n $AcrName --query id -o tsv
$kvId = az keyvault show -n $KeyVaultName --query id -o tsv
az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role "AcrPull" --scope $acrId -o none
az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User" --scope $kvId -o none
Write-Host "RBAC role assignments can take a minute to propagate."
Start-Sleep -Seconds 30

# Step 6: Container App — image, ingress, scaling, Key Vault-backed secrets
Write-Host "[6/6] Deploying Container App '$AppName'..." -ForegroundColor Yellow
$kvUri = az keyvault show -n $KeyVaultName --query properties.vaultUri -o tsv
$jwtRef = "jwt-signing-key=keyvaultref:${kvUri}secrets/JwtSigningKey,identityref:$identityId"
$aiRef  = "appinsights-conn=keyvaultref:${kvUri}secrets/AppInsightsConnectionString,identityref:$identityId"

az containerapp create `
    --name $AppName `
    --resource-group $ResourceGroup `
    --environment $EnvironmentName `
    --image "$acrLoginServer/$ImageTag" `
    --registry-server $acrLoginServer `
    --registry-identity $identityId `
    --user-assigned $identityId `
    --ingress external `
    --target-port 8080 `
    --min-replicas 1 `
    --max-replicas 5 `
    --scale-rule-name "http-concurrency-rule" `
    --scale-rule-type "http" `
    --scale-rule-http-concurrency 50 `
    --secrets $jwtRef $aiRef `
    --env-vars "Jwt__SigningKey=secretref:jwt-signing-key" "AppInsights__ConnectionString=secretref:appinsights-conn" "ASPNETCORE_ENVIRONMENT=Production" `
    --output table

$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup --query "properties.configuration.ingress.fqdn" -o tsv

Write-Host "`n=== Deployment Successful ===" -ForegroundColor Green
Write-Host "App FQDN: https://$fqdn" -ForegroundColor Cyan
Write-Host "Root endpoint: https://$fqdn/ (returns 'Quotes API is running!')"
