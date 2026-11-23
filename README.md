# ValuPro

Property valuation management for valuation firms: assignments, site inspection, valuation, reports and billing.
.NET 10 API + Angular 22 frontend, hosted on Azure. Built in phases. See the *ValuPro Development Plan* doc.

**Status: Phase 1 in progress.** Done: Phase 0 foundation (sign-in, tenant-ready data layer, app shell) and
**User Management**: user list with search/filters/paging, add/edit with status (Active / On Leave / Inactive),
temporary passwords with forced change at first sign-in, password reset, and the editable role × module
permission matrix. **Settings**: company profile, branding (logo, report watermark, colours that restyle the
app and login page), email (SMTP with test send) and SMS gateway, GST and numbering, notification preferences.
**Clients**: individuals, builders, bank branches and advocates with codes (CL0001…), PAN/GSTIN checks and
duplicate detection, search and filters, view/edit/remove, and bulk import from CSV or Excel with a row-by-row
check before anything is saved. **Banks & Branches**: empanelled banks (quick pick of common banks), branches with
IFSC checks, per-bank valuation rules (rate basis, distress/realizable %, depreciation) editable one by one or in a
table, and a dynamic questionnaire per bank. Other sidebar modules show which phase builds them.

## Layout

| Path | What |
| --- | --- |
| `backend/src/ValuPro.Domain` | Entities and rules, no dependencies (`TenantEntity`, roles, permissions) |
| `backend/src/ValuPro.Application` | Use-case contracts and validation (`ICurrentUser`, `ITenantContext`, `IFileStorage`, auth DTOs) |
| `backend/src/ValuPro.Infrastructure` | EF Core + SQL Server, ASP.NET Identity, JWT, tenant filters, audit, Azure Blob storage |
| `backend/src/ValuPro.Api` | ASP.NET Core host, controllers, permission authorization |
| `backend/tests/ValuPro.UnitTests` | xUnit tests |
| `frontend` | Angular app (standalone components, signals, zoneless) |
| `infra` | Bicep for one Azure environment |
| `.github/workflows` | CI on every PR; manual deploy per environment |

## Run locally

Needs: .NET SDK 10, Node 24 and SQL Server (Express is fine). Uploaded files are stored in
`backend/src/ValuPro.Api/App_Data/files` during development (`Storage:LocalPath`); set `Storage:ConnectionString`
instead to use Azurite or a real storage account.

1. Database: `backend/src/ValuPro.Api/appsettings.Development.json` points at `.\SQLEXPRESS01` with Windows auth. Change `ConnectionStrings:Default` if yours differs.
2. Secrets (stored in your user profile, never in the repo):

   ```powershell
   cd backend/src/ValuPro.Api
   dotnet user-secrets set "Jwt:SigningKey" "<48+ random characters>"
   dotnet user-secrets set "Seed:AdminPassword" "<password for admin@valupro.local>"
   ```

3. API (applies migrations and seeds the firm, 7 roles and the first admin on start):

   ```powershell
   dotnet run --project backend/src/ValuPro.Api --launch-profile http
   ```

   API explorer: http://localhost:5091/scalar · health: http://localhost:5091/health

4. Web (proxies `/api` to the API):

   ```powershell
   npm --prefix frontend start
   ```

   Open http://localhost:4200 and sign in as `admin@valupro.local`.

## Tests

```powershell
dotnet test backend
npm --prefix frontend test -- --watch=false
```

## Key design rules

- **Tenant-ready from day one.** Business entities inherit `TenantEntity`. EF Core stamps `TenantId` on insert, filters every query by the current tenant, and refuses to move rows between tenants. Today there is one tenant (`Tenancy:DefaultTenantId`). For SaaS, `TenantContext` resolves the tenant from the subdomain instead.
- **Soft delete.** Deleting a `TenantEntity` sets `IsDeleted`; queries hide it.
- **Readable numbers** (client codes now; assignment and invoice numbers next) come from `INumberSequence`: per
  firm, never reused, safe when two people save at once.
- **Permissions, not roles, guard features.** Use `[HasPermission(Permissions.Clients.Manage)]` on the API and `data: { permission }` on routes. Roles map to permissions via role claims (the role × module matrix), so admins can change access without a deployment.
- **Files** go through `IFileStorage`; paths are `tenants/{tenantId}/…` and read links are short-lived SAS URLs.
- **Migrations:** `dotnet ef migrations add <Name> -p src/ValuPro.Infrastructure -s src/ValuPro.Api -o Persistence/Migrations` (run in `backend`). CI fails if the model has changes without a migration.

## Azure

`infra/main.bicep` creates, per environment: App Service (API, managed identity), Static Web App (Angular, `/api` linked to the App Service), Azure SQL (Entra-only auth, serverless outside prod), Storage (no shared keys), Key Vault (JWT key), Application Insights.

Before the first deploy:
1. Create a resource group and an Entra app registration with a federated credential for this GitHub repository (OIDC). Grant it Contributor and User Access Administrator on the resource group.
2. Create an Entra group for SQL admins, add the app registration to it, and set its id and name in `infra/main.<env>.bicepparam`.
3. In GitHub → Settings → Environments → `dev`, set the variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` and the secret `JWT_SIGNING_KEY`.
4. Run the **Deploy** workflow. It provisions infrastructure, applies migrations, grants the API access to SQL, and deploys the API and web.
5. Seed the first admin in Azure by setting `Seed__AdminEmail` / `Seed__AdminPassword` (Key Vault reference) and `Database__MigrateOnStartup=true` for one start, then remove them.

## Access rules

- Every request re-checks the token against the user's current status and security stamp (cached ≤ 60 s per server).
  Deactivating a user, resetting their password or their own password change ends existing sessions; role and
  permission edits apply without signing in again.
- Users cannot change their own role or deactivate themselves; the last active Admin cannot be demoted or deactivated.
- The Admin role always has every permission (locked in the matrix). Granting Manage/Approve also grants View.
- New users and password resets get a one-time temporary password, shown once to the admin; the user must choose
  their own at next sign-in.

## Known limits (still open)

- Access tokens (60 min) are kept in `localStorage`; refresh tokens in an httpOnly cookie are still to do.
- With several API instances, an access change can take up to 60 s to reach the other instances (in-memory cache).
- Email addresses are unique across all tenants (fine for one firm; revisit for SaaS).
- Temporary passwords are handed over by the admin; emailing them automatically is still to do (SMTP is now configurable).
- SMS settings are stored but nothing is sent yet; delivery comes with notifications in Phase 4.
- SMTP password and SMS API key are encrypted with ASP.NET Data Protection. App Service keeps the key ring for
  you; if the keys are ever lost, the secrets read as "not set" and need re-entering.
