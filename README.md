# ValuPro

Property valuation management for valuation firms: assignments, site inspection, valuation, reports and billing.
.NET 10 API + Angular 22 frontend, hosted on Azure. Built in phases. See the *ValuPro Development Plan* doc.

**Status: Phase 2 in progress** (Phase 1 complete). Done: Phase 0 foundation (sign-in, tenant-ready data layer, app shell) and
**User Management**: user list with search/filters/paging, add/edit with status (Active / On Leave / Inactive),
temporary passwords with forced change at first sign-in, password reset, and the editable role × module
permission matrix. **Settings**: company profile, branding (logo, report watermark, colours that restyle the
app and login page), email (SMTP with test send) and SMS gateway, GST and numbering, notification preferences.
**Clients**: individuals, builders, bank branches and advocates with codes (CL0001…), PAN/GSTIN checks and
duplicate detection, search and filters, view/edit/remove, and bulk import from CSV or Excel with a row-by-row
check before anything is saved. **Banks & Branches**: empanelled banks (quick pick of common banks), branches with
IFSC checks, per-bank valuation rules (rate basis, distress/realizable %, depreciation) editable one by one or in a
table, and a dynamic questionnaire per bank. **Properties**: register with survey/CTS/plot numbers, areas, zone,
usage and GPS ("Use my location" on phones), plus each property's valuation history. **Assignments**: numbers
like VAL/2026/0001 (prefix from Settings, per-year counter), bank/branch, applicant and property linked to the
masters or typed, valuer and reviewer, the 7-stage workflow with a timeline, a stage pipeline, list and Kanban
board views. **Rate Master** (Phase 2): location (market) rates per locality and zone, circle / Jantri / Ready Reckoner
rates, construction rates (CPWD / PWD / bank) and depreciation rules (straight line or written-down value, with an
age-band table). Rates carry an "effective from" date: a newer date keeps the old rate as history, and the current
rate is the latest one already in force. Rates import from CSV or Excel with the same row-by-row check.
**Site Inspection** (Phase 2): a phone-first, step-by-step site visit form (visit and person met, locality, boundaries
as per documents vs at site, occupancy, building and specifications, measurements, approvals and deviations, the bank's
own questions, tagged photos, remarks). It captures the location at site and flags it when it is more than 300 m from
the property's saved location. **Photos** (Phase 2): a firm-wide gallery (search, type, dates, "needs a look" for site
photos with no location or taken far from the property) and a page per case to choose the report photos and their
order (drag, or arrows on phones), add photos received later, and download them as a ZIP numbered in report order
with a photos.csv of times and locations. Camera photos get a date/time/location stamp printed below the picture.
Other sidebar modules show which phase builds them.

## Site inspection rules

- One inspection per assignment. The assigned valuer, or someone with Assignments: Manage (plus Inspections: Manage), records it.
- It can be edited while the case is New or Site Visit Scheduled. Submitting checks everything required (location, person met,
  boundaries, occupancy, building details unless land only, a measured area, approvals, required bank questions, front and
  approach-road photos), locks it and moves the case to "Inspection Done". That stage can only be reached this way.
  Sending the case back to "Site Visit Scheduled" makes it editable again.
- Every change autosaves. Each save carries a version number; a save from an older copy (another phone) is refused, not merged.
- Works with weak signal: the form is kept on the phone (localStorage) and photos wait in IndexedDB until they upload.
  Photos are resized on the phone (1600 px JPEG plus a 360 px thumbnail) and a retried upload is not saved twice.
- Recorded details are stored as one JSON document (`Inspections.Data`), so fields can be added as banks' formats arrive.

## Photo rules

- Photos can be added, re-tagged, captioned, reordered and chosen for the report until the case is Submitted, by the
  case's valuer or reviewer, or anyone with Assignments: Manage (plus Inspections: Manage).
- Once the inspection is submitted, its photos cannot be deleted (they are the record of the visit); leave them out of
  the report instead.
- Only photos taken with the camera inside the app get a location and the printed stamp. Photos chosen from the
  gallery or uploaded at the office have neither, because the app can't know where or when they were taken.

## Assignment workflow rules

- Cases move one stage at a time. Sending back needs a reason; cancelling needs a reason and can be undone (reopen).
- Scheduling the site visit needs its date and time.
- Managers (Assignments: Manage) can do everything. The assigned valuer and reviewer can move their own cases.
- Passing Quality Review (→ Submitted) needs Reports: Approve. Marking as invoiced needs Billing or Assignment management.
- Only brand-new cases can be deleted; others are cancelled so their history stays. Numbers are never reused.
- Clients, banks, branches and properties with open cases cannot be removed.
- Due dates and "overdue" use the India calendar day, whatever the server's time zone.

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
- **Own UI components, no UI kit.** The frontend uses its own design (from the prototype) and shared pieces in
  `src/app/shared` (`LocalTable` for in-browser search/sort/paging, `th[vpSort]`, `vp-pager`, `vp-tabs`,
  `input[vpAmount]` for ₹ amounts with Indian grouping, `vp-choice` / `vp-multi` / `vp-yes-no` chips, plus the dialog,
  toast and typeahead in `core/ui`). Every text box, dropdown and notes box gets one style from `styles.scss` (no
  component restyles them); wrap a group in `.dense` (tables) or `.large` (sign-in) for the other sizes, and use
  `.fld`, `.fld-pre` (₹ before) / `.fld-suf` (% after) and `.search` for layout. On phones boxes use 16px text so iOS doesn't zoom.
  PrimeNG was tried and dropped: from v22 it needs a commercial PrimeUI licence key. Add MIT libraries only for heavy
  widgets (Angular CDK, Chart.js, FullCalendar, Leaflet, TipTap).
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
