# Code explanation

## Start with one request

When a user opens `/crm/customers`, `RecordsController.Index` receives the request. It asks `CrmService.Rows` for the authorised customer data. `CrmService.Query` applies the role and ownership condition before reading records. Entity Framework Core sends a parameterised query to SQLite. The controller passes a page of DTOs to the Razor view, which produces the HTML table.

A save uses the reverse path: Razor form → controller → validated form model → CRM service → EF Core → database. The JSON API calls the same service. This avoids having one set of rules for the website and a different set for the API.

## Files to explain in order

1. `Program.cs`: dependency injection, database connection, Identity, password policy, cookies, anti-forgery, rate limiting and routes.
2. `Models/Crm.cs`: fields stored for each module. `AppUser` extends the framework's `IdentityUser`; there is no separate password table.
3. `Data/ApplicationDbContext.cs`: Identity tables, CRM tables, relationships and unique customer indexes. `CrmRow` holds shared fields; EF's table-per-concrete-type mapping keeps a separate table for each CRM module. GUID IDs are created in application code.
4. `ViewModels/Forms.cs`: the fields a user may submit, their validation annotations, and safe output DTOs.
5. `Services/CrmService.cs`: ownership checks, business rules, lead conversion, soft deletion, search and pagination.
6. `Services/AuditService.cs`: append a safe audit record to the same EF context. CRM changes and their audit records are saved together.
7. `Controllers/RecordsController.cs`: connects reusable CRM screens to the service. `module` identifies the module and must match the fixed list in `CrmCatalog.Modules`.
8. `Controllers/ApiController.cs`: JSON equivalents with 201 for creation, 400 for invalid input, 401 for anonymous access, 403 for forbidden actions, 404 for invisible or missing records, and 409 for conflicts.
9. `Controllers/AccountController.cs` and `UsersController.cs`: Identity login/registration and administrator-only access changes.
10. `Controllers/HomeController.cs`, `ReportsController.cs` and `Views`: authorise data, calculate metrics and display the result.

## Naming used in the code

Names say what the value is: `dbContext` (EF database context), `crmService`, `userManager`, `form` (submitted RecordForm), `filter` (RecordFilter from the query string), `record` (database entity), `row` (RecordDto in a list), `transaction`, `currentUser`, `valueBeforeChange` (audit old value). Methods are verbs: `GetScopedQuery`, `FindRecordAsync`, `SaveRecordAsync`, `ConvertLeadAsync`, `MarkCompletedAsync`, `BuildTeamPerformanceAsync`.

## Role dashboards and menus

`DashboardService.BuildAsync` builds one `DashboardViewModel`. Every list comes from `CrmService`, so a Sales Executive's numbers are already limited to their own records. Admin-only sections (users, lockouts, failed sign-ins, audit feed) and manager/admin team performance are filled only for those roles. `Views/Home/Index.cshtml` shows the sections for the signed-in role. The sidebar sections come from `ViewModels/NavigationMenu.cs`. Hiding a menu link is only convenience; `[Authorize(Roles = ...)]` and the ownership filter are the real protection.

## Why client and server validation are both needed

Razor generates `data-val` attributes from C# annotations. jQuery unobtrusive validation reads those attributes and shows inline errors. HTML input constraints and `validation.js` add date, numeric and related-record checks. These help the user, but a caller can bypass the browser. The MVC/API model validation and `CrmService.Save` therefore repeat important checks before writing to the database.

Customer contact uniqueness has two checks: a friendly service-level check and a unique database index. The index still prevents duplicates if two requests arrive together.

## How authorisation works

`[Authorize]` blocks anonymous callers. `[Authorize(Roles="Admin")]` protects administration. `CrmService.Query` restricts SalesExecutive data by `OwnerId`. Both record lookup and modification reuse it, so changing an ID in the URL does not bypass access control. The API returns 404 for missing and out-of-scope records to avoid disclosing their existence.

Changing a user's role, password or active status updates the Identity security stamp. Every authenticated request checks that the current account is active and that its stamp matches the cookie. An old session loses access after an administrator changes it.

## Lead conversion transaction

A lead must already be Qualified. The service finds an exact matching customer by its normalised email and phone. Conflicting contacts or ownership are rejected. Otherwise it creates the customer, marks the lead Converted and creates an opportunity if ExpectedValue is positive. The changes and audit events commit in one transaction. A second conversion attempt is rejected because the status is already Converted.

## Pipeline calculations

Open pipeline is the sum of amounts with status Open. Weighted pipeline is `Amount * Probability / 100`. A won opportunity requires probability 100; a lost opportunity requires 0. Monthly sales groups Won opportunities by their recorded outcome date, rather than assuming their expected close date was the actual sale date.

## Security choices

Identity performs password hashing and verification. Forms carry anti-forgery tokens, including cookie-authenticated API writes via the `X-CSRF-TOKEN` header. EF Core parameterises database access. Razor escapes stored text. CSV export neutralises leading formula characters. No API or audit payload contains password hashes or reset tokens. Application code cannot modify or delete an existing audit entity.

## Limitations to explain honestly

This is a single-team assignment application. Lists are authorised in SQL, then a small in-memory list is searched and paginated for simplicity. For a large CRM, push the remaining filtering and pagination into SQL. Concurrent edits currently use last-write-wins; a larger deployment should add row versioning. Database migrations, external backups, monitoring and a supported patched runtime should be part of a real production deployment.

## Suggested viva questions

- Why use a DTO when an EF entity already exists?
- What stops a Sales Executive from changing another person's record?
- Why does hiding a menu item not provide security?
- Why do you need a unique index as well as a duplicate query?
- Why is lead conversion inside a transaction?
- What is the difference between authentication and authorisation?
- What does weighted pipeline measure?
- How does a changed security stamp invalidate a session?
- Why can a GET request be unauthenticated only for login, registration and CSRF setup?
- What would you change for multiple teams or lakhs of records?
