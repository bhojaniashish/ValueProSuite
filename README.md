# ValuPro

Property valuation management for valuation firms: assignments, site inspection, valuation, reports and billing.
.NET 10 API + Angular 22 frontend, hosted on Azure. Built in phases. See the *ValuPro Development Plan* doc.

**Status: Phase 4 in progress** (Phases 0–3 done; Billing, Dashboard and Calendar built). Done: Phase 0 foundation (sign-in, tenant-ready data layer, app shell) and
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
**Documents** (Phase 2): per case, a checklist of the usual documents for the property type, and each document perused
with its number, date, issuing office and whether the original was seen, a copy only, or it was not provided (with the
reason, as reports state). Files (PDF or photo) are optional and keep earlier versions. Documents from earlier cases on
the same property can be reused. A firm-wide list flags documents expiring within 30 days or already expired.
**Valuation Engine** (Phase 2): composite rate (flats, offices, shops) or land + depreciated building (houses, plots,
sheds), with ± adjustments and extras. It is pre-filled from the inspection (lower of measured and document areas,
structure, age), the Rate Master (locality band, circle rate, construction rate, depreciation rule) and the bank's rules
(realizable / distress %, depreciation method, rate basis). It gives fair market, realizable, distress, guideline
(government) and insurance values, the value in words (lakh / crore), the working, and warnings banks ask about.
**Reports** (Phase 3): one default bank-style valuation report (PDF, A4) built from the case: letterhead, general details,
property and locality, boundaries, documents perused, building and approvals, measurements, the valuation working and
values (with the value in words), the bank's own questions, opinion, declaration, signature block and a photo annexure.
Drafts are watermarked; approving produces the final PDF signed with the valuer's signature image (each user draws or
uploads theirs under **My signature**). Every generated PDF is kept as a version.
**Stage inspections / re-inspections** (Phase 3): construction-loan cases follow an earlier case on the same property.
The inspection gets a "Construction progress" step (weighted stages of work, quick done-% chips, overall completion,
comparison with the last visit); the valuation works out value of work done, the bank's eligible share and the release
recommended now (carrying cost, finance % and disbursements from the previous visit); the report prints as a
STAGE INSPECTION / RE-INSPECTION REPORT with the stage table and a disbursement table. Market value is optional.
**Billing** (Phase 4): GST tax invoices for submitted cases (one or several of a bank's cases per invoice), billed to the
bank branch or the client, with CGST + SGST or IGST by state, SAC code, round-off and the amount in words; payments with
TDS; outstanding, overdue, collected and unbilled figures for the financial year and by bank; invoice PDF.

**Dashboard** (Phase 4): the day at a glance for everyone or just your own cases: open, overdue, due-this-week and
submitted-this-month counts; cases that need attention (late, due within 3 days, or waiting on your review); site visits
for the next 7 days; the pipeline by stage; open cases by bank; team workload per valuer; six months of cases received vs
submitted with fees; recent activity; and, for billing users, invoiced, collected, outstanding and ready-to-bill figures.
Every figure links to the filtered assignment or billing list. Users without the dashboard permission see their module tiles.

**Calendar** (Phase 4): site visits, report due dates, payments due (billing users) and hand-made follow-ups, reminders
and meetings, in month, week and list views, for everyone or just you. Book a visit for a new case (it moves to Site Visit
Scheduled) or move a booked one, from a dialog or by dragging it to another day; moves are logged on the case timeline.
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

## Document rules

- Documents can be recorded and changed until the case is Submitted, by anyone with Assignments: Manage (the office), or
  by the case's valuer or reviewer with Inspections: Manage.
- "Not provided" needs a reason. Uploading a file to a not-provided document makes it "copy only".
- A new upload is a new version; older versions stay and can be opened. Removing a document keeps its files in history.
- Reusing a document from an earlier case of the same property lists the same stored files; nothing is copied.

## Valuation rules

- Values are always worked out on the server (`ValuationCalculator`), both for the live figures and when saving, so the
  page and the saved values can't disagree. Market value is rounded to the nearest ₹1,000; realizable and distress
  values are percentages of it, also rounded to ₹1,000. Guideline value = area ÷ 10.7639 × circle rate per sq.m.
- Land + building: land area × rate, plus each floor's area × construction rate less depreciation (straight line or
  written-down value, from age, life and salvage %). Insurance value is the rebuild cost without land.
- Warnings: market value more than 10% below the guideline value, a rate outside the Rate Master band, measured and
  document areas more than 10% apart.
- The case's valuer or reviewer, or a manager (with Valuation: Manage), can change it until the report is Submitted.
  A save from an older copy is refused. "Draft Prepared" needs a saved valuation.

## Report rules

- A report needs a submitted inspection and a saved valuation. Missing checklist documents, report photos or the
  valuer's signature are shown as reminders.
- "Generate draft" makes a watermarked, unsigned PDF; the first one moves the case from "Inspection Done" to
  "Draft Prepared". "Quality Review" needs a draft.
- "Approve & sign" (Reports: Approve, at Quality Review) makes the final PDF signed with the case **valuer's** signature,
  records who approved it, and moves the case to "Submitted". Users with Reports: Approve can only reach "Submitted" this way.
- Report wording (dates, place, opinion, declaration, photos per page) can be edited until the report is submitted.
  Keeping the standard declaration stores nothing, so it follows the inspection date.
- **Emailing to the bank:** once signed, "Email the report to the bank" sends the signed PDF as an attachment through the
  firm's SMTP (Settings → Email & SMS). To is pre-filled from the branch's email (else the last recipients), a copy goes to
  the sender, replies go to the sender, and the wording is editable. Every attempt is logged (sent or failed, shown under
  "Sent to the bank"); a sent email also goes on the case timeline. Needs Reports: Manage; PDFs over 18 MB are refused.
- In development `Email:PickupDirectory` (appsettings.Development.json → `App_Data/mail`) writes every email as an `.eml`
  file instead of sending it, so nothing leaves the machine. Leave it unset in production.
- **Report formats (bank templates):** Settings → Templates lists the firm's formats; each sets the title, subtitle,
  "To" line, which sections print and in what order (with custom headings), which values the valuation shows, the
  opinion line and declaration (with placeholders such as {date}, {value}, {bank}) and limiting conditions, with a live
  sample preview. A bank picks its format (and empanelment no.) on the bank form; banks without one use the default
  format, and with no formats the standard layout. Old free-text format names became formats in the migration.
- Signatures are images for now (`SignatureMethod.Image`); Aadhaar eSign and DSC are planned as other methods.
- **PDF library licence:** reports use QuestPDF under its Community licence, which is free only while the firm's annual
  gross revenue is under USD 1,000,000 (public-sector bodies and listed companies are not eligible). Buy a QuestPDF
  licence within 90 days of passing the threshold, before offering ValuPro as SaaS to larger firms. (PDFsharp/MigraDoc
  became AGPL from version 6.5, so it was not used.) The default font (Lato, bundled with QuestPDF) prints ₹.

## Stage inspection rules

- Case type is Valuation, Stage inspection or Re-inspection. A stage / re-inspection case can follow an earlier case,
  which must be on the same property (the list offers cases linked to the property register).
- Overall completion = Σ (stage weight × done %). Weights must total 100 before the inspection can be submitted; the
  current work and "as per approved plan" are required, while age, condition and measurements are not.
- Value of work done = estimated cost × completion. Eligible = work done × finance % (capped at the sanctioned loan).
  Release = eligible − already disbursed, rounded to ₹1,000 and never below zero. Warnings: no cost, completion lower
  than the last visit, or more already paid than is eligible.
- A new visit starts from the previous one: stage table, structure, boundaries, cost, finance %, sanctioned loan, and
  "already disbursed" = previous disbursed + previous release.

## Billing rules

- A draft invoice has no number and can be edited or deleted. Issuing gives it the next number for its financial year
  (`{prefix}/26-27/0001`, April–March) and makes it final; corrections are made by cancelling (with a reason) and issuing
  a new one. Cancelled invoices are kept and their numbers stay used.
- One invoice covers one bank's cases. A case can be on only one issued invoice. Issuing moves submitted cases to
  "Invoice Generated"; cancelling moves them back to "Submitted" (unless another issued invoice still bills them).
- The recipient's name, address, GSTIN and state are copied onto the invoice (from the branch or the client, editable).
  Same state as the firm (Settings → GST state code) = CGST + SGST half each; another state = IGST. GST is worked out to
  the paisa and the total rounded to the rupee (round-off shown). Charging GST needs the firm's GSTIN.
- Payments record the amount received and the TDS deducted (banks deduct 10% under 194J; the form offers it); both settle
  the invoice. Payments can't exceed what is outstanding; an invoice with payments can't be cancelled until they are
  removed. Unpaid past the due date = overdue.
- Settings → GST also holds the SAC code (997222), payment due days, payment details and invoice terms printed on invoices.

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
