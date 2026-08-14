# Day 5 — Task 3: Azure Container Apps Fundamentals

## Commands used

```
az group create -n thinkschool-rg -l centralindia
az containerapp env create -n thinkschool-env -g thinkschool-rg -l centralindia
```

Note: `thinkschool-rg` already existed from prior work, so the group-create call was idempotent — no new resource group was actually created, just reused.

## Deliverable — `az containerapp env show -n thinkschool-env -g thinkschool-rg -o json`

```json
{
  "id": "/subscriptions/<subscription-id>/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env",
  "location": "Central India",
  "name": "thinkschool-env",
  "properties": {
    "appInsightsConfiguration": null,
    "appLogsConfiguration": {
      "destination": "log-analytics",
      "logAnalyticsConfiguration": {
        "customerId": "ff1d9236-4a4b-4525-8e49-8a1e37f81add",
        "sharedKey": null
      }
    },
    "customDomainConfiguration": {
      "certificateKeyVaultProperties": null,
      "certificatePassword": null,
      "certificateValue": null,
      "customDomainVerificationId": "4B91D14CA80924D7634F1427CFA42AF1D8EBB73BA5BE9F545AC28C745A502E58",
      "dnsSuffix": null,
      "expirationDate": null,
      "subjectName": null,
      "thumbprint": null
    },
    "daprAIConnectionString": null,
    "daprAIInstrumentationKey": null,
    "daprConfiguration": { "version": "1.16.4-msft.11" },
    "defaultDomain": "whitestone-71ebd55e.centralindia.azurecontainerapps.io",
    "eventStreamEndpoint": "https://centralindia.azurecontainerapps.dev/subscriptions/<subscription-id>/resourceGroups/thinkschool-rg/managedEnvironments/thinkschool-env/eventstream",
    "infrastructureResourceGroup": null,
    "ingressConfiguration": null,
    "kedaConfiguration": { "version": "2.18.1" },
    "openTelemetryConfiguration": null,
    "peerAuthentication": { "mtls": { "enabled": false } },
    "peerTrafficConfiguration": { "encryption": { "enabled": false } },
    "provisioningState": "Succeeded",
    "publicNetworkAccess": "Enabled",
    "staticIp": "20.204.236.55",
    "vnetConfiguration": null,
    "workloadProfiles": [
      { "enableFips": false, "name": "Consumption", "workloadProfileType": "Consumption" }
    ],
    "zoneRedundant": false
  },
  "resourceGroup": "thinkschool-rg",
  "systemData": {
    "createdAt": "2026-08-14T07:54:17.7270469",
    "createdBy": "<redacted-email>",
    "createdByType": "User",
    "lastModifiedAt": "2026-08-14T08:46:39.5094704",
    "lastModifiedBy": "<redacted-email>",
    "lastModifiedByType": "User"
  },
  "type": "Microsoft.App/managedEnvironments"
}
```

## What this session taught

An **environment** is the shared boundary for one or more container **apps** — it holds the networking (here, `staticIp` and the auto-assigned `defaultDomain`), the KEDA/Dapr runtime versions, and the logging sink; apps deployed into it share that infrastructure. A **revision** is a versioned, immutable snapshot of a single app's config+image, created each time you update the app — it's the unit that actually gets scaled and routed to, not the app itself. The **Log Analytics workspace** Azure auto-generated (`workspace-thinkschoolrgFoN1`) is where all container stdout/stderr and system logs from apps in this environment get shipped (`appLogsConfiguration.destination: "log-analytics"`) — without it, `az containerapp logs` and diagnostics queries have nowhere to pull from.

## What would break this

- **Unregistered resource providers** — `Microsoft.App` or `Microsoft.OperationalInsights` not registered on the subscription would fail the environment create outright (registration can lag several minutes after `az provider register`).
- **Region support** — Container Apps isn't available in every Azure region; picking one that doesn't support it fails at create time.
- **Name collisions on `defaultDomain`** — the environment's default domain (`whitestone-71ebd55e.centralindia.azurecontainerapps.io` here) includes a random suffix precisely because it must be globally unique; a fixed custom domain would need its own uniqueness check.
- **Quota/subscription limits** — Consumption plan workload profiles are capped per subscription/region; hitting that cap blocks new environments.

## Cleanup

Tear down when done to stop billing (not run as part of this submission):

```
az group delete -n thinkschool-rg --yes --no-wait
```
