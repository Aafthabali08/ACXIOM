# Verification record

Verified locally on 7 October 2026 with .NET SDK 9.0.109, ASP.NET Core 9.0.8, SQLite and Python 3, using `bash tests/run_all_checks.sh` (the same script the GitHub Actions pipeline runs) on a fresh database.

| Step | Result |
| --- | --- |
| Release build of the solution with warnings treated as errors | ✅ 0 warnings, 0 errors |
| Unit tests – `tests/AcxiomCRM.Tests` (xUnit, in-memory SQLite) | ✅ 65 passed |
| API and workflow checks – `tests/check_api.py` | ✅ 209 passed |
| Security checks – `tests/check_security.py` | ✅ 70 passed |
| Server log during the run | ✅ no errors |

## What each layer covers

- **Unit tests:** opportunity amount/probability/close-date rules with the PDF messages, stage–status–probability consistency, weighted amount, follow-up date rule, exactly-one related record, Missed only when overdue, completing a follow-up moves a New lead to Contacted, rescheduling audit, customer email/phone uniqueness, lead workflow transitions, conversion (customer + opportunity, only once), ownership scoping, assignment only to active Sales Executives, related-record owner rule, soft delete with dependency check, audit old/new values, audit rows cannot be changed or deleted, search, phone/email/length validation messages, dashboard periods, pagination, CSV formula safety, report access matrix, pipeline (stage-wise/owner-wise/weighted) and lead conversion columns.
- **API and workflow checks:** anonymous access, the three role dashboards with their named sections and the eight mandatory cards, every period filter, page access per role, ownership and 404 for other owners, customer duplicates, lead workflow and dropdown, conversion, opportunity rules, follow-up rules and rescheduling, Mark done (API and form, CSRF required, no off-site redirect, other owners blocked), related-record panels, prefilled "Schedule follow-up", owner auto-select data, required customer on opportunities, every report and export, global search scope, user sorting, team page, clean-up and logout.
- **Security checks:** registration validation, self-registration cannot choose a role, lockout after five failures, locked filter and unlock, role change ends old sessions, name-only edits keep the session, deactivation, admin password reset, admin cannot demote themselves, profile rename, password change (wrong current, weak new, success, other sessions signed out), login return address (local only), hashed password storage, no secrets in the audit log, and every table/column name from PDF section 17.3.

## Browser walkthrough (built-in browser)

- **Link crawl per role:** every link reachable from each role's pages was opened and checked for status, server-error text and page title – Sales Executive 113 pages + 6 CSV exports, Manager 165 pages + 7 CSV exports, Admin 175 pages + 8 CSV exports. No broken links and no link to a page the role may not open. Manager blocked from `/Users`, `/Users/Edit`, `/Users/Roles` and the audit report.
- **Forms by hand:** register (required fields, password mismatch, success → Sales Executive dashboard), login with keyboard only, add user (client required fields, server "password required", success with role), opportunity (PDF messages for amount 0, probability 101, past close date, required customer; stage Won/Lost sets status and probability; picking a customer sets "Assigned to"), lead form (inline email/mobile errors, allowed statuses only, Contacted → Qualified → Convert), follow-up prefilled from a customer, Mark done from the agenda, delete with confirm (cancel keeps, accept soft-deletes, linked records block with a message), report filters + pagination + export keep the filters, profile rename updates the header, light/dark theme switch with chart redraw, phone width with the slide-in menu, global search across modules.
- **Console:** no JavaScript errors.

## Bugs found and fixed during verification

1. Saving any user from Users → Edit refreshed the security stamp, so an admin renaming themselves was signed out. The stamp now changes only for role, status, email or password changes.
2. Submitting the profile form with an empty name crashed the page. Initials now come from the saved name through a null-safe helper.
3. Record forms showed a native browser bubble and the inline message at the same time. Inline validation now runs first.
4. Signing in always opened the dashboard even after following a link to a specific page. Login now returns to that page (local addresses only).
5. Opportunity forms did not require a customer until the server answered. The customer field is now required on the client too.
6. Managers and admins had to pick the matching owner by hand after choosing a related record, otherwise the save failed. "Assigned to" now follows the chosen record's owner.
7. The probability range was enforced only by the form annotation. `CrmService` now enforces it as well, so every caller gets the rule.
8. "Log out" wrapped onto two lines at tablet width.

Changes made after a page-by-page review against the assignment PDF: database names from section 17.3, the eight mandatory dashboard cards with an "All time" default, assignment only to Sales Executives, follow-up completion updating a New lead, lead conversion columns, stage-wise/owner-wise pipeline report, PDF validation wording on the client, global search and user sorting. See `docs/REQUIREMENTS.md`.

These checks show the assignment behaviour. They are not a penetration test or production certification.
