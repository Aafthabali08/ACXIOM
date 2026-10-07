# Assignment compliance map

Source: `AcxiomCRM_Complete_Project_Assignment.pdf` (19 pages). Every section of the PDF is listed below with the place in the code that satisfies it and the automated check that proves it. Section 17 (pages 14–19) is the mandatory ASP.NET Core part and decides the technology: ASP.NET Core MVC, Identity, EF Core (SQLite), Razor, Bootstrap and Chart.js.

Tests: `tests/check_api.py` (API) and `tests/check_security.py` (SEC). Status ✅ = implemented and covered by a passing check or verified in the browser.

## 2. Module structure (page 2)

| # | PDF module | Where it lives | Status |
|---|---|---|---|
| 1 | AcxiomCRM shell | `Views/Shared/_Layout.cshtml`, `ViewModels/NavigationMenu.cs`, `wwwroot/css/site.css` | ✅ |
| 2 | Authentication & Authorization | `AccountController`, `Services/LoginService.cs`, Identity setup in `Program.cs` | ✅ |
| 3 | Dashboard | `HomeController`, `Services/DashboardService.cs`, `Views/Home/Index.cshtml` | ✅ |
| 4 | Customer Management | `/crm/customers` (`RecordsController`, `CrmService`) | ✅ |
| 5 | Lead Management | `/crm/leads`, conversion in `CrmService.ConvertLeadAsync` | ✅ |
| 6 | Follow-Up Management | `/crm/followups`, `CrmService.MarkCompletedAsync`, header reminder bell | ✅ |
| 7 | User & Role Management | `UsersController`, `Views/Users/*` | ✅ |
| 8 | Opportunity | `/crm/opportunities` | ✅ |
| 9 | Audit Log | `Services/AuditService.cs`, `AuditLog` table, Audit report | ✅ |
| 10 | REST API | `Controllers/ApiController.cs`, `docs/API.md` | ✅ |
| 11 | Reports | `Services/ReportService.cs`, `ReportsController` | ✅ |
| 17.2 | Activity Management (Call, Meeting, Email, Task) | `/crm/activities` | ✅ |

## 3. Layered architecture (page 3)

| PDF layer | Implementation |
|---|---|
| Presentation | Razor views, layout, Bootstrap, `wwwroot/js` (client validation, charts, theme) |
| Application | Controllers, `ViewModels` (form models, `RecordDto`, `PagedResult`), model validation |
| Domain / Business | `Models` (entities), `Services/CrmService.cs` (rules, workflows, status transitions), `CrmCatalog` (statuses, stages, roles) |
| Data access | `Data/ApplicationDbContext.cs` (EF Core = repository + unit of work), transactions in conversion, user admin and registration |
| Database | SQLite: relationships, unique indexes, restrict-delete foreign keys, table/column names from 17.3 |
| Security | Identity, cookie settings, `[Authorize]` + default role policy, anti-forgery, rate limiting, security headers, security-stamp checks |
| Cross-cutting | `AuditService`, global exception handler, configuration (`appsettings.json`), notifications (reminder bell, alerts) |

## 4. Functional module requirements (pages 4–6)

### 4.1 AcxiomCRM shell
| Requirement | Implementation | Proof |
|---|---|---|
| Responsive layout, navigation, header, user menu, notifications, footer | `_Layout.cshtml`: glass sidebar (drawer on phones), top bar, profile avatar, follow-up bell with due count, footer | Browser at 1440px and 375px |
| Menu items by role | `NavigationMenu.ForRole` – Admin / Manager / Sales Executive sections | API: "Admin dashboard has section…", "Manager menu has no user administration", "Sales menu hides team and admin sections" |
| Global search/filter | Header search → `SearchController` searches all five modules in the user's scope | API: "Global search finds own records", "…hides other owners' records" |
| Consistent styling, validation messages, alerts, confirmations, pagination | Shared CSS tokens, `_Alerts.cshtml`, `data-confirm` on delete/convert, `PagedResult` on every list | Browser |
| Protect pages from anonymous access | Default authorization policy requires sign-in and a CRM role | API: "Anonymous visitor is sent to the login page", `/api/customers → 401` |

### 4.2 Authentication & Authorization
| Requirement | Implementation | Proof |
|---|---|---|
| Login / logout | `AccountController.Login/Logout`, `/api/auth/login`, `/api/auth/logout` | API + SEC |
| Username or email | `LoginService` tries email, then username | SEC login checks |
| Hashed passwords only | ASP.NET Core Identity (PBKDF2) | SEC: "Identity stores an encoded hash only" |
| Password policy | 8+ chars, upper, lower, digit, symbol (`Program.cs`) | SEC: weak password rejected (register and change password) |
| Lockout | 5 failures → 15 min (configurable in `appsettings.json`) | SEC: "Five failed attempts lock the account" |
| Role + ownership authorization | `[Authorize(Roles=…)]`, `CrmService.GetScopedQuery` | API: ownership and 404 checks |
| Audit success and failure | `LoginService` writes Login / Failed Login / Lockout | Admin dashboard events, audit report |
| Secure cookie + anti-forgery | HttpOnly, SameSite=Strict, Secure in production, 30 min sliding; global `AutoValidateAntiforgeryToken` | API: POST without token → 400 |

### 4.3 Dashboard
| Requirement | Implementation | Proof |
|---|---|---|
| Role-based KPIs (customers, open leads, open opportunities, pending follow-ups, pipeline) | 8 mandatory cards + Pending / Due today / Overdue follow-up counters | API: "Dashboard has mandatory card: …" |
| Admin: user/activity/security statistics | System access cards, users by role, recently added users, latest audit events | API admin section checks |
| Manager: team pipeline, leads, opportunity performance | Team pipeline vs won chart, lead funnel, team performance table, needs attention, closing soon | API manager section checks |
| Sales Executive: assigned customers, leads, opportunities, upcoming follow-ups | My numbers, today's agenda with Mark done, closing soon, recent activities | API sales section checks |
| Today / This Week / This Month / Custom Range | `_PeriodFilter` (+ All time default) | API: each period 200, invalid range 400 |
| Charts from server-authorized data | Chart data comes from scoped `CrmService` lists | Sales chart only shows own deals (browser) |

### 4.4 Customer Management
| Requirement | Implementation |
|---|---|
| Create, view, edit, search, filter | `/crm/customers` list (search, status, owner, date, sort), details, edit |
| Name, email, phone, address, status, assigned sales executive, notes | `Customer` entity + `Edit.cshtml` (plus code, company, city, state) |
| Invalid email/phone rejected | `[EmailAddress]`, Indian mobile regex `^[6-9]\d{9}$`, client and server |
| Duplicate checking | Service check + unique DB indexes on email, phone, code |
| Track creation, modification, status changes | `CreatedDate`, `CreatedBy`, `ModifiedDate`; audit Create / Update / Status Change |
| History and related activities | Details page: history timeline, follow-ups, activities, opportunities |

### 4.5 Lead Management
| Requirement | Implementation |
|---|---|
| Source, contact details, assigned sales executive, status, priority, notes | `Lead` entity + form |
| Statuses New, Contacted, Qualified, Unqualified, Converted, Lost | `CrmCatalog.StatusesFor("leads")` |
| Assignment to Sales Executive | Owner must be an active Sales Executive (dropdown and server rule) – API: "Records can only be assigned to a Sales Executive" |
| Invalid transitions blocked | `CrmCatalog.NextLeadStatuses`; dropdown offers only allowed next steps – API checks |
| Conversion of qualified leads | `ConvertLeadAsync` (transaction, customer reuse, optional opportunity, audit) – API checks |

### 4.6 Follow-Up Management
| Requirement | Implementation |
|---|---|
| Against customers, leads and opportunities | Exactly one of CustomerId / LeadId / OpportunityId |
| Date, subject, type, status, notes, assigned user | `FollowUp` entity (`FollowUpDate`, `Subject`, `FollowUpType`, `Remarks`, `AssignedTo`) |
| Planned, Completed, Missed, Cancelled | `CrmCatalog`; Missed only when overdue |
| Upcoming and overdue | Dashboard agenda, "Overdue" filter, bell badge, follow-up report |
| No date before today for new/planned | Server rule + client `notpast` rule – API: yesterday → 400 |
| Completion and rescheduling audited | Audit "Status Change" and "Reschedule" – API: "Mark done is recorded in the audit log" |

### 4.7 User & Role Management
| Requirement | Implementation |
|---|---|
| Create, edit, activate/deactivate, search | `/Users` (search, role, state filters, sort, pagination), `/Users/Edit` |
| Exactly one role | Role dropdown; old roles removed before the new one is added |
| Only admins | `[Authorize(Roles = "Admin")]` – API: manager/sales → 403 |
| Reset password | Optional password field on edit – SEC: "admin password reset" |
| Account and lockout status | Status and "Locked until" columns, Unlock button |
| No password hashes exposed | `RecordDto` and views never include Identity fields – API: "DTOs exclude password fields" |

### 4.8 Opportunity
| Requirement | Implementation |
|---|---|
| Name, customer, owner, stage, amount, probability, expected close date, source, notes | `Opportunity` entity + form |
| Stages Qualification, Proposal, Negotiation, Won, Lost | `CrmCatalog.Stages`; status follows stage |
| Weighted pipeline = Amount × Probability / 100 | `RecordDto.WeightedAmount`, dashboard, pipeline report |
| Amount > 0, probability 0–100, no past close date for open deals | `ValidateOpportunityAsync` + client rules – API: 0, -1, 101, -1 %, yesterday → 400 |

### 4.9 Audit Log
| Requirement | Implementation |
|---|---|
| Login success/failure, logout, CRUD, role changes, password/security events, status changes | Actions: Login, Failed Login, Lockout, Logout, Register, Create, Update, Delete, Status Change, Reschedule, Convert, Role Change, Password Reset, Password Change, Profile Update, Unlock |
| User, timestamp, action, module, record id, result, metadata | `AuditLog` (UserId, CreatedDate, Action, EntityName, RecordId, Result, OldValue, NewValue, IpAddress) |
| Append-only | `ApplicationDbContext.SaveChangesAsync` refuses modify/delete of audit rows |
| Filter by user, module, action, date | Audit report filters |

### 4.10 REST API · 4.11 Reports
See sections 10 and 11 below.

## 5. Validation (pages 7–8)

| Rule | Client side | Server side | Message (PDF 5.4) |
|---|---|---|---|
| Required | Razor `data-val-required` + HTML `required` | `[Required]` + service checks | Field-specific |
| Email | `data-val-email` | `[EmailAddress]` + service | "Enter a valid email address." |
| Phone (10-digit Indian mobile) | regex + `pattern` | `[RegularExpression]` | "Enter a valid 10-digit Indian mobile number." |
| Length | `maxlength`, `data-val-length` | `[StringLength]`, `[MaxLength]` on entities | Field-specific |
| Date: close date not past (open deal) | `notpast` rule in `validation.js` | `ValidateOpportunityAsync` | "Expected Close Date cannot be in the past." |
| Date: follow-up not before today (new/planned) | `notpast` rule | `ValidateFollowUpOrActivityAsync` | "Follow-up date cannot be earlier than today." |
| Numeric: amount > 0 | `positive` rule | service | "Opportunity Amount must be greater than 0." |
| Numeric: probability 0–100 | `data-msg-min/max`, `data-val-range` | `[Range]` | "Probability must be between 0 and 100." |
| Foreign keys and referenced records | – | `EnsureSameOwnerAsync` (record must be visible and same owner) | – |
| Tampered requests | – | Ownership filter, owner rules, status lists | API: other owner → 404, assign to others → 403 |
| No DB internals in messages | – | `DbUpdateException` → friendly 409; global exception handler | – |

## 6. Authentication & security (page 8)

All of 6.1 and 6.3 are covered (see 4.2). 6.2: minimum length 8, mixed character classes, no plain text, nothing secret logged (SEC: "Passwords never appear in audit payloads"), secure change flow (My profile, current password required) and admin reset. Preventing reuse of recent passwords is marked optional in the PDF and is not implemented.

## 7. Role-based authorization (page 9)

| Module | Admin | Manager | Sales Executive |
|---|---|---|---|
| Dashboard | Full (company + security) | Team | Own |
| Customers / Leads / Opportunities / Follow-ups / Activities | Full | Team | Own/assigned only (`OwnerId` filter in SQL) |
| User management | Full | No | No |
| Role management / permissions page | Full | No | No |
| Audit log | Full | Limited: User activity report (CRM events only) | No |
| REST API | Authorized endpoints | Authorized endpoints | Authorized endpoints (own data) |
| Reports | All | Management/team incl. user activity, team page | Own reports |

`/Users/Roles` shows this matrix inside the app.

## 8. Core workflows (pages 9–10)

| Workflow | Steps in the app |
|---|---|
| Lead-to-Customer | Create lead → assign (Sales Executive) → Contacted → Qualified → Convert (customer + opportunity) → audit "Convert" |
| Opportunity | Create → amount → probability → close date → move stages → Won/Lost (probability forced 100/0) → outcome date recorded |
| Follow-Up | Schedule (prefilled from a record page) → date → assign → Mark done / Missed / Reschedule → **related New lead becomes Contacted** → audited |
| User Administration | Create user → role → active/inactive → password policy and lockout → audit Create/Role Change/Password Reset/Unlock |

## 9 and 17.3. Entities and database names

Table and column names match section 17.3 exactly (mapped in `ApplicationDbContext.MapToAssignmentNames`). SEC checks every column with `PRAGMA table_info`.

| Table | Columns (PDF names) | Extra columns |
|---|---|---|
| Customer | CustomerId, CustomerCode, CustomerName, Email, Phone, CompanyName, Address, City, State, Status, CreatedDate, CreatedBy | AssignedTo, Notes, ModifiedDate, IsDeleted |
| Lead | LeadId, LeadCode, LeadName, Email, Phone, CompanyName, Source, Status, ExpectedValue, CreatedDate, AssignedTo | Priority, CustomerId (converted to), Notes, CreatedBy, ModifiedDate, IsDeleted |
| Opportunity | OpportunityId, OpportunityName, CustomerId, LeadId, Amount, Stage, Probability, ExpectedCloseDate, Status, CreatedDate, AssignedTo | Source, OutcomeDate, Notes, CreatedBy, ModifiedDate, IsDeleted |
| FollowUp | FollowUpId, CustomerId, LeadId, FollowUpDate, FollowUpType, Remarks, Status, AssignedTo | Subject, OpportunityId (PDF 4.6), CreatedBy, CreatedDate, ModifiedDate, IsDeleted |
| Activity | ActivityId, ActivityType, Subject, Description, ActivityDate, CustomerId, LeadId, AssignedTo, Status | CreatedBy, CreatedDate, ModifiedDate, IsDeleted |
| AuditLog | AuditLogId, UserId, Action, EntityName, RecordId, OldValue, NewValue, CreatedDate, IpAddress | Result |
| User (Identity `AspNetUsers`) | Id, Name, Email, PasswordHash, IsActive, AccessFailedCount, LockoutEnd, CreatedDate | Identity's own fields |
| Role (Identity `AspNetRoles` + `AspNetUserRoles`) | Id, Name | – |

Relationships: Opportunity → Customer (required), Opportunity → Lead, FollowUp → Customer/Lead/Opportunity, Activity → Customer/Lead, Lead → Customer (after conversion), every CRM row → AspNetUsers via AssignedTo. All foreign keys use `Restrict`; records are soft deleted (`IsDeleted`) so history is never broken. Unique indexes: Customer.Email, Customer.Phone, Customer.CustomerCode, Lead.LeadCode.

17.4: no custom password table – ASP.NET Core Identity with the extended `AppUser`.

## 10 and 17.14. REST API (pages 11, 18)

| PDF endpoint | Implemented | Access |
|---|---|---|
| POST /api/auth/login | ✅ rate limited (15/min/IP) | Public |
| POST /api/auth/logout | ✅ | Authenticated |
| GET/POST /api/customers, GET/PUT/DELETE /api/customers/{id} | ✅ | Authorized, scoped |
| GET/POST /api/leads (+ GET/PUT/DELETE by id, POST convert) | ✅ | Authorized, scoped |
| GET/POST /api/opportunities (+ by id) | ✅ | Authorized, scoped |
| GET/POST /api/followups (+ by id, POST complete) | ✅ | Authorized, scoped |
| /api/activities (all verbs) | ✅ extra | Authorized, scoped |
| GET /api/reports/pipeline | ✅ | Manager/Admin; Sales Executive gets own scope ("configured scope") |

DTOs only, payload validation, codes 200/201/400/401/403/404/409/429, consistent `{ error, errors }` objects, no stack traces. Documented in `docs/API.md`.

## 11 and 4.11. Reports (pages 6, 11)

| Report | Key data | Access |
|---|---|---|
| Customers | Customer, code, status, owner, created date, company, contact | All (scoped) |
| Leads | Source, status, priority, owner, value, **Converted (Yes/No), Converted to customer** | All (scoped) |
| Follow-ups | Date, status incl. **Overdue**, owner, type, related record | All (scoped) |
| Opportunities | Customer, stage, status, amount, probability, close date, owner | All (scoped) |
| Pipeline | **Stage-wise, Owner-wise, Stage × owner and Total** rows with amount and weighted value | All (scoped) |
| Conversion | Leads converted vs not, opportunities won vs lost, rates | All (scoped) |
| User activity | Actions per user, creates, updates, last activity | Admin, Manager |
| Audit | All security and business events with old/new values and IP | Admin only |

Every report: search, user/date/status (or module/action/result) filters, sorting, pagination and full CSV export (formula-injection safe).

## 12. UI/UX standards (page 12)

Responsive Bootstrap layout (desktop, tablet, phone); same navigation everywhere; inline validation messages; success/error alerts; confirmation before delete/convert; search, filter, sort and pagination on every list (customers, leads, opportunities, follow-ups, activities, users, reports); labels, `aria-*`, skip link and keyboard focus styles; generic error pages, no technical exceptions.

## 13 and 17.19. Acceptance checklist and final scenario

| Step | How it is verified |
|---|---|
| 1. Anonymous access blocked | API: login redirect, API 401 |
| 2. Register/login → dashboard | SEC: "Registration opens the Sales Executive dashboard" |
| 3. Invalid email/phone blocked in browser | Browser: inline messages, save blocked |
| 4. Crafted request rejected by server | API: bad email/phone JSON → 400 |
| 5. Amount ≤ 0 rejected | API |
| 6. Probability > 100 rejected | API |
| 7. Past close date rejected (open deal) | API |
| 8. Follow-up before today rejected | API |
| 9. Sales Executive sees only own scope | API ownership checks |
| 10. Manager has team pipeline/reports | API: manager dashboard, team page, user activity report |
| 11. Admin has user/role/audit administration | API: /Users, /Users/Roles, audit report |
| 12. CRUD creates audit entries | API: audit export checks |
| 13. /api/customers returns authorized JSON | API |
| 14. Dashboard cards and Chart.js charts | API mandatory card checks + browser (no console errors) |

## 14. Security checklist (pages 12–13)

Password hashing ✅ · password policy ✅ · lockout ✅ · role authorization ✅ · server validation ✅ · anti-forgery ✅ · sensitive data kept out of logs ✅ · HTTPS + HSTS outside Development ✅ · API authorization and validation ✅ · audit logging ✅ · least privilege (self-registration = Sales Executive; managers cannot administer users) ✅ · no database credentials in source (SQLite file path only; production admin from environment variables) ✅.

## 16 and 17.20. Completion standard

The project shows CRM workflows (lead → customer → opportunity, follow-up completion), client and server validation, business rules, Identity authentication, three-role authorization, dashboard analytics with Chart.js, audit logging, a REST API and the required folder structure (Controllers, Models, Data, Services, ViewModels, Views).

## Known limits (stated honestly)

- Optional items not built: preventing reuse of recent passwords (PDF 6.2 "consider"), email-based self-service password reset (admin reset is the supported workflow per 4.7), multi-team hierarchy.
- Lists are filtered by role in SQL, then searched and paged in memory. Fine for an assignment; a large deployment should page in SQL.
- The schema is created with `EnsureCreated`; a production system should use EF migrations.
