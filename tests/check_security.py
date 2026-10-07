"""Security acceptance checks: registration, lockout, role change, deactivation, password reset and change.

Run against a disposable local development database, at least a minute after check_api.py
(sign-in endpoints allow 15 attempts per minute per IP):
    python3 tests/check_security.py http://127.0.0.1:5080

It leaves one inactive test account behind, plus its audit history.
"""
import os
import re
import sqlite3
import time
import urllib.parse
import uuid
from pathlib import Path

from crm_client import CrmClient, check, expect
import crm_client

test_email = "security-" + uuid.uuid4().hex[:8] + "@example.test"
# The full pipeline uses its own database file; a manual run checks the normal development database.
project_folder = Path(__file__).resolve().parent.parent
database_file = project_folder / os.environ.get("CHECK_DATABASE_FILE", "App_Data/AcxiomCRM.db")

# ---------------------------------------------------------------- registration
new_member = CrmClient()
new_member.refresh_csrf()
weak_password_page = expect(new_member, "/Account/Register", 200, "POST",
                            {"Name": "Security check", "Email": test_email, "Password": "weak", "Confirm": "weak"}, as_form=True)
check("at least" in weak_password_page.lower() or "minimum" in weak_password_page.lower(), "Weak registration password is rejected")
mismatch_page = expect(new_member, "/Account/Register", 200, "POST",
                       {"Name": "Security check", "Email": test_email, "Password": "Acxiom@123", "Confirm": "Acxiom@124"}, as_form=True)
check("Both passwords must match." in mismatch_page, "Password confirmation must match")
# The extra Role field is ignored: self-registration always gives Sales Executive.
dashboard_page = expect(new_member, "/Account/Register", 200, "POST",
                        {"Name": "Security check", "Email": test_email, "Password": "Acxiom@123", "Confirm": "Acxiom@123", "Role": "Admin"}, as_form=True)
check("Sales Executive dashboard" in dashboard_page and "Welcome aboard" in dashboard_page, "Registration opens the Sales Executive dashboard")
new_member.refresh_csrf()
expect(new_member, "/Users", 403)
expect(new_member, "/api/auth/logout", 200, "POST", {})

# ---------------------------------------------------------------- lockout and unlock
new_member.refresh_csrf()
for attempt_number in range(5):
    expect(new_member, "/api/auth/login", 401, "POST", {"email": test_email, "password": "Wrong@123"})
lockout_response = expect(new_member, "/api/auth/login", 401, "POST", {"email": test_email, "password": "Acxiom@123"})
check("locked" in lockout_response["error"].lower(), "Five failed attempts lock the account")

admin = CrmClient()
admin.login("admin@acxiom.test")
users_page = expect(admin, "/Users?q=" + urllib.parse.quote(test_email), 200)
test_user_id = re.search(r'/Users/Edit/([^"?]+)', users_page).group(1)
check("Locked until" in users_page, "Admin user list shows the lock")
locked_filter_page = expect(admin, "/Users?state=locked", 200)
check(test_email in locked_filter_page, "Locked filter finds the account")
expect(admin, "/Users/Unlock", 200, "POST", {"id": test_user_id}, as_form=True)
new_member.login(test_email)

# ---------------------------------------------------------------- role change ends the old session
expect(admin, "/Users/Edit", 200, "POST", {"Id": test_user_id, "Name": "Security check", "Email": test_email, "Role": "Manager", "Active": "true"}, as_form=True)
expect(new_member, "/api/customers", 401)
new_member.login(test_email)
expect(new_member, "/Reports?report=user-activity", 200)
manager_dashboard = expect(new_member, "/", 200)
check("Sales Manager dashboard" in manager_dashboard, "Promoted user now gets the manager dashboard")
expect(new_member, "/Users", 403)

# A name-only edit must not sign the user out.
expect(admin, "/Users/Edit", 200, "POST", {"Id": test_user_id, "Name": "Security check renamed", "Email": test_email, "Role": "Manager", "Active": "true"}, as_form=True)
expect(new_member, "/api/customers", 200)

# ---------------------------------------------------------------- deactivation
expect(admin, "/Users/Edit", 200, "POST", {"Id": test_user_id, "Name": "Security check", "Email": test_email, "Role": "Manager", "Active": "false"}, as_form=True)
expect(new_member, "/api/customers", 401)
new_member.refresh_csrf()
expect(new_member, "/api/auth/login", 401, "POST", {"email": test_email, "password": "Acxiom@123"})

# ---------------------------------------------------------------- admin password reset
expect(admin, "/Users/Edit", 200, "POST",
       {"Id": test_user_id, "Name": "Security check", "Email": test_email, "Role": "SalesExecutive", "Active": "true", "Password": "Reset@456"}, as_form=True)
new_member.refresh_csrf()
expect(new_member, "/api/auth/login", 200, "POST", {"email": test_email, "password": "Reset@456"})
new_member.refresh_csrf()
expect(new_member, "/api/customers", 200)

# ---------------------------------------------------------------- admin cannot lock themselves out
admin_users_page = expect(admin, "/Users?q=admin%40acxiom.test", 200)
admin_user_id = re.search(r'/Users/Edit/([^"?]+)', admin_users_page).group(1)
self_demotion_page = expect(admin, "/Users/Edit", 200, "POST",
                            {"Id": admin_user_id, "Name": "Aarav Sharma", "Email": "admin@acxiom.test", "Role": "Manager", "Active": "true"}, as_form=True)
check("You cannot remove your own administrator access." in self_demotion_page, "Admin cannot remove their own admin role")
expect(admin, "/Users", 200)

# ---------------------------------------------------------------- my profile: name and password change
# Sign-in endpoints allow 15 requests per minute per IP. Wait for a fresh window instead of loosening the limit.
print("Waiting 61 seconds for the sign-in rate limit window to reset...")
time.sleep(61)
profile_page = expect(new_member, "/Account/Profile", 200)
check("Sales Executive" in profile_page and test_email in profile_page, "Profile shows email and role")
expect(new_member, "/Account/UpdateProfile", 400, "POST", {"Details.Name": ""}, as_form=True)
renamed_page = expect(new_member, "/Account/UpdateProfile", 200, "POST", {"Details.Name": "Security Tester"}, as_form=True)
check("Your profile has been updated." in renamed_page and "Security Tester" in renamed_page, "User can change their own name")

second_browser = CrmClient()
second_browser.login(test_email, "Reset@456")
wrong_current_page = expect(new_member, "/Account/ChangePassword", 400, "POST",
                            {"Password.CurrentPassword": "Wrong@999", "Password.NewPassword": "Change@789", "Password.ConfirmPassword": "Change@789"}, as_form=True)
check("Current password is not correct." in wrong_current_page, "Wrong current password is rejected")
weak_new_page = expect(new_member, "/Account/ChangePassword", 400, "POST",
                       {"Password.CurrentPassword": "Reset@456", "Password.NewPassword": "weakpass", "Password.ConfirmPassword": "weakpass"}, as_form=True)
check("Passwords must have at least one" in weak_new_page, "Weak new password is rejected")
changed_page = expect(new_member, "/Account/ChangePassword", 200, "POST",
                      {"Password.CurrentPassword": "Reset@456", "Password.NewPassword": "Change@789", "Password.ConfirmPassword": "Change@789"}, as_form=True)
check("Password changed." in changed_page, "User can change their own password")
new_member.refresh_csrf()
expect(new_member, "/api/customers", 200)
expect(second_browser, "/api/customers", 401)
second_browser.refresh_csrf()
expect(second_browser, "/api/auth/login", 401, "POST", {"email": test_email, "password": "Reset@456"})

# ---------------------------------------------------------------- login returns to the page the user asked for
returning_user = CrmClient()
returning_user.refresh_csrf()
leads_page = expect(returning_user, "/Account/Login", 200, "POST",
                    {"Email": "sales@acxiom.test", "Password": crm_client.DEMO_PASSWORD, "returnUrl": "/crm/leads?status=New"}, as_form=True)
check("My leads" in leads_page and "Search name, company" in leads_page, "Login goes back to the page that was asked for")
offsite_user = CrmClient()
offsite_user.refresh_csrf()
offsite_page = expect(offsite_user, "/Account/Login", 200, "POST",
                      {"Email": "sales@acxiom.test", "Password": crm_client.DEMO_PASSWORD, "returnUrl": "https://example.com/phish"}, as_form=True)
check("Sales Executive dashboard" in offsite_page, "Login ignores an off-site return address")

# Leave the test account inactive.
expect(admin, "/Users/Edit", 200, "POST",
       {"Id": test_user_id, "Name": "Security check (inactive test account)", "Email": test_email, "Role": "SalesExecutive", "Active": "false"}, as_form=True)

# ---------------------------------------------------------------- stored data
with sqlite3.connect(database_file) as database:
    stored_hash = database.execute("SELECT PasswordHash FROM AspNetUsers WHERE Email = ?", (test_email,)).fetchone()[0]
    check(stored_hash not in ("Change@789", "Reset@456") and len(stored_hash) > 50, "Identity stores an encoded hash only")
    audit_payloads = " ".join(str(row) for row in database.execute("SELECT OldValue, NewValue FROM AuditLog").fetchall())
    check("Reset@456" not in audit_payloads and "Change@789" not in audit_payloads and crm_client.DEMO_PASSWORD not in audit_payloads,
          "Passwords never appear in audit payloads")
    password_events = database.execute("SELECT Result FROM AuditLog WHERE Action = 'Password Change' AND RecordId = ?", (test_user_id,)).fetchall()
    check(sorted(row[0] for row in password_events) == ["Failed", "Failed", "Success"], "Password changes are audited", password_events)

    # Database names follow section 17.3 of the assignment PDF.
    expected_columns = {
        "Customer": ["CustomerId", "CustomerCode", "CustomerName", "Email", "Phone", "CompanyName", "Address", "City", "State", "Status", "CreatedDate", "CreatedBy"],
        "Lead": ["LeadId", "LeadCode", "LeadName", "Email", "Phone", "CompanyName", "Source", "Status", "ExpectedValue", "CreatedDate", "AssignedTo"],
        "Opportunity": ["OpportunityId", "OpportunityName", "CustomerId", "LeadId", "Amount", "Stage", "Probability", "ExpectedCloseDate", "Status", "CreatedDate", "AssignedTo"],
        "FollowUp": ["FollowUpId", "CustomerId", "LeadId", "FollowUpDate", "FollowUpType", "Remarks", "Status", "AssignedTo"],
        "Activity": ["ActivityId", "ActivityType", "Subject", "Description", "ActivityDate", "CustomerId", "LeadId", "AssignedTo", "Status"],
        "AuditLog": ["AuditLogId", "UserId", "Action", "EntityName", "RecordId", "OldValue", "NewValue", "CreatedDate", "IpAddress"],
    }
    for table_name, required_columns in expected_columns.items():
        actual_columns = [column[1] for column in database.execute(f"PRAGMA table_info({table_name})").fetchall()]
        missing_columns = [column for column in required_columns if column not in actual_columns]
        check(not missing_columns, f"Table {table_name} has every column from the assignment", missing_columns)

print(f"\n{crm_client.passed_checks} security checks passed.")
