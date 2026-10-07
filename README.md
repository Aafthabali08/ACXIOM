# AcxiomCRM

A small ASP.NET Core MVC CRM built against the 19-page AcxiomCRM assignment. It includes the mandatory modules added in section 17, including Activity Management, registration and ASP.NET Core Identity.

## Run locally

Install the .NET 9 SDK, then run from this folder:

```sh
dotnet restore
dotnet run
```

Open http://localhost:5080. The first start creates the SQLite database and development sample records. No separate database server is needed. Bootstrap, jQuery validation and Chart.js are stored locally, so the screens do not depend on a CDN.

The current project targets .NET 9 because that SDK is installed in this workspace. Before internet-facing deployment, move to a currently supported .NET release and current matching Identity/EF packages; see `docs/DEPLOYMENT.md`.

## Development accounts

| Role | Email | Password |
| --- | --- | --- |
| Admin | admin@acxiom.test | Acxiom@123 |
| Manager | manager@acxiom.test | Acxiom@123 |
| Sales Executive | sales@acxiom.test | Acxiom@123 |
| Second Sales Executive | sales2@acxiom.test | Acxiom@123 |

These credentials are for local assignment demonstration only. Demo seeding runs only in Development. Real passwords are stored as ASP.NET Core Identity hashes, never as plain text. The database and Data Protection keys are excluded from source control.

## Included

- Identity registration, login and logout; configurable lockout; password policy; protected cookies and anti-forgery checks.
- Admin, Manager and SalesExecutive roles with server-enforced ownership checks. Self-registration always assigns SalesExecutive.
- Customer, lead, opportunity, follow-up and activity CRUD, related records and history.
- Qualified lead conversion, customer duplicate checking and an optional opportunity created from a positive expected value.
- Date, amount, probability, email, Indian mobile, required-field and length validation.
- Three role dashboards on one URL, each with its own named menu sections:
  - **Administrator** – Control Centre, User Administration, Security & Compliance, Business Records, Insights. Dashboard: system access cards (users, active, locked, failed sign-ins), company snapshot, pipeline chart, users by role, latest audit events, monthly sales, team table.
  - **Sales Manager** – Overview, Team Management, Sales Pipeline, Customer Relations. Dashboard: team snapshot, pipeline vs won per executive, lead funnel, team performance table, overdue follow-ups, deals closing in 30 days.
  - **Sales Executive** – My Workspace, My Sales, My Insights. Dashboard: my numbers, today's agenda with one-click *Mark done*, my lead status, my pipeline, deals closing soon, monthly sales, recent activities.
- Team performance page (Manager/Admin): leads, conversions, open/weighted pipeline, won value, win rate, activities and overdue follow-ups per executive.
- My profile for every role: change display name, change password (current password required; other sessions are signed out).
- Black-and-white "liquid glass" UI with a light/dark theme switch, remembered per browser. Works down to phone width.
- Search, filters, sorting, pagination, eight reports and CSV export.
- Admin user creation, role changes, deactivation, password reset and account unlock.
- Audited changes, including safe old/new values, actor, time, record ID and IP address.
- Secured JSON APIs using explicit DTOs, shared business rules and meaningful HTTP codes.

## Project structure

| Folder | Purpose |
| --- | --- |
| Controllers | HTTP requests, authentication, screens, APIs and reports |
| Models | Identity user and CRM database entities |
| Data | EF Core database setup and development seed data |
| Services | Ownership, workflow rules, conversion and auditing |
| ViewModels | Form input models, validation attributes and response DTOs |
| Views | Razor screens with Bootstrap and inline validation |
| wwwroot | Local CSS, JavaScript, icons and third-party libraries |
| tests | xUnit project (`tests/AcxiomCRM.Tests`), Python acceptance checks and `run_all_checks.sh` |
| docs | Requirements mapping, API guide, explanation and deployment notes |

## Demonstrate the main workflow

1. Log in as `sales@acxiom.test`.
2. Add a lead with valid contact details and a positive expected value.
3. Edit it to Contacted, then Qualified.
4. Open its details and select Convert lead. A customer and opportunity are created together.
5. Schedule a follow-up for that customer and record a call under Activities.
6. Complete the follow-up. Move the opportunity to Won with probability 100.
7. View the pipeline and conversion reports.
8. Log in as Admin to inspect the audit events. Compare the second executive's visible records.

## Checks

Three layers of tests, all run by one script and by the GitHub Actions pipeline (`.github/workflows/ci.yml`) on every push and pull request:

| Layer | What it covers | Command |
| --- | --- | --- |
| Unit tests (xUnit, 65) | Business rules from the PDF on `CrmService` with an in-memory database, validation messages, helpers, reports, CSV safety | `dotnet test AcxiomCRM.sln` |
| API and workflow checks (Python, 209) | Every role, page access, ownership, CRUD, conversion, Mark done, reports, exports, search | `python3 tests/check_api.py http://127.0.0.1:5080` |
| Security checks (Python, 70) | Registration, password policy, lockout/unlock, role change, deactivation, password reset and change, database column names | `python3 tests/check_security.py http://127.0.0.1:5080` |

Run everything (build with warnings as errors → unit tests → start the app on a throw-away database → API checks → security checks):

```sh
bash tests/run_all_checks.sh
```

It takes about three minutes because sign-in is limited to 15 attempts per minute per IP and the security checks wait for a fresh window. Run the Python scripts on their own only against a disposable local database: they create and soft-delete test records and leave an inactive test account.

See `docs/TEST_RESULTS.md` for the recorded verification, `docs/REQUIREMENTS.md` for the PDF compliance map and `docs/EXPLAIN.md` for a code walkthrough.

## Scope choices

- One shared sales team: managers manage all business records; sales executives see only their assignments. There is no separate team hierarchy in the assignment's mandatory data model.
- Deletes are soft deletes, with dependency checks and audit history. The UI does not provide an undelete function. Unique customer contacts remain reserved after deletion.
- Related records share their owner's scope. Reassignment is blocked when it would leave dependent records inconsistent.
- Date rules use India time. Stored timestamps and audit displays use UTC. CRM date filters use India dates; audit date filters use UTC dates.
- The selected dashboard period filters records by creation date. Monthly sales uses actual outcome dates across the last six months. Pending reminders always show current outstanding work.
- Notifications are in-app reminders. No emails or SMS are sent. Admin password reset is the supported reset workflow; there is no email-based forgot-password service.
- Database tables and columns use the exact names from section 17.3 of the assignment (`Customer.CustomerName`, `Lead.AssignedTo`, `FollowUp.FollowUpDate`, `AuditLog.IpAddress` …). See `docs/REQUIREMENTS.md` for the full section-by-section compliance map.
- CRM records are always assigned to an active Sales Executive, as the assignment describes.
- The initial database is created with `EnsureCreated`. A persistent production system should use a reviewed EF migration workflow for later schema changes.

## Original code and dependencies

The application-specific logic, views and styles were written for this assignment. ASP.NET templates and third-party libraries remain third-party work, with their notices retained. Do not describe framework/vendor code as your own. There is no reliable guarantee that an AI or plagiarism detector will classify code in a particular way.
