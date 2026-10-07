"""End-to-end acceptance checks for AcxiomCRM.

Run against a disposable local development database:
    python3 tests/check_api.py http://127.0.0.1:5080

The script creates its own test records and soft-deletes them at the end.
"""
import datetime
import json
import re
import uuid

from crm_client import CrmClient, check, expect
import crm_client


def unique_suffix():
    return uuid.uuid4().hex[:6]


def unique_mobile():
    # Indian mobile numbers start with 6-9 and have 10 digits.
    return "9" + str(uuid.uuid4().int)[:9]


india_today = datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=5, minutes=30))).date()
tomorrow = str(india_today + datetime.timedelta(days=1))
yesterday = str(india_today - datetime.timedelta(days=1))

# ---------------------------------------------------------------- sign in
anonymous = CrmClient()
expect(anonymous, "/api/customers", 401)
login_page = expect(anonymous, "/", 200)  # redirected to the login page
check("Sign in to your workspace" in login_page, "Anonymous visitor is sent to the login page")

admin = CrmClient()
admin_id = admin.login("admin@acxiom.test")
rahul = CrmClient()
rahul_id = rahul.login("sales@acxiom.test")
neha = CrmClient()
neha_id = neha.login("sales2@acxiom.test")
manager = CrmClient()
manager.login("manager@acxiom.test")

# ---------------------------------------------------------------- role dashboards and menus
admin_dashboard = expect(admin, "/", 200)
check("Administrator dashboard" in admin_dashboard and "System access" in admin_dashboard, "Admin sees the admin dashboard")
for required_card in ["Total customers", "Total leads", "Open leads", "Total opportunities", "Open opportunities",
                      "Won opportunities", "Lost opportunities", "Total pipeline value", "All time"]:
    check(required_card in admin_dashboard, "Dashboard has mandatory card: " + required_card)
for admin_section in ["User Administration", "Security &amp; Compliance", "Business Records", "Users by role", "Latest security &amp; business events", "Team performance"]:
    check(admin_section in admin_dashboard, "Admin dashboard has section: " + admin_section)

manager_dashboard = expect(manager, "/", 200)
check("Sales Manager dashboard" in manager_dashboard and "Team snapshot" in manager_dashboard, "Manager sees the manager dashboard")
for manager_section in ["Team Management", "Sales Pipeline", "Customer Relations", "teamChart", "Needs attention", "Deals closing in 30 days"]:
    check(manager_section in manager_dashboard, "Manager dashboard has section: " + manager_section)
check("User Administration" not in manager_dashboard and "/Users/Edit" not in manager_dashboard, "Manager menu has no user administration")

sales_dashboard = expect(rahul, "/", 200)
check("Sales Executive dashboard" in sales_dashboard and "My numbers" in sales_dashboard, "Sales executive sees the personal dashboard")
for sales_section in ["My Workspace", "My Sales", "Today's agenda", "My lead status", "Closing in 30 days", "My recent activities"]:
    check(sales_section in sales_dashboard, "Sales dashboard has section: " + sales_section)
check("Team performance" not in sales_dashboard and "System access" not in sales_dashboard, "Sales menu hides team and admin sections")

for period in ["all", "today", "week", "month"]:
    expect(rahul, "/?period=" + period, 200)
expect(rahul, f"/?period=custom&from={tomorrow}&to={yesterday}", 400)
expect(rahul, "/?period=bogus", 400)

# ---------------------------------------------------------------- page access by role
for page_url in ["/", "/Team", "/crm/customers", "/crm/leads", "/crm/opportunities", "/crm/followups", "/crm/activities",
                 "/Users", "/Users/Edit", "/Users/Roles", "/Account/Profile", "/Reports?report=pipeline", "/Reports?report=audit"]:
    expect(admin, page_url, 200)
for module_name in ["customers", "leads", "opportunities", "followups", "activities"]:
    expect(admin, "/crm/" + module_name + "/create", 200)
expect(admin, "/crm/unknown", 404)
expect(manager, "/Team", 200)
expect(manager, "/Users", 403)
expect(rahul, "/Users", 403)
expect(rahul, "/Team", 403)
expect(rahul, "/Reports?report=audit", 403)
expect(rahul, "/Account/Profile", 200)

# ---------------------------------------------------------------- ownership
all_customers = expect(admin, "/api/customers", 200)["items"]
rahul_customers = expect(rahul, "/api/customers", 200)["items"]
check(len(all_customers) > len(rahul_customers) and all(row["ownerId"] == rahul_id for row in rahul_customers),
      "Sales executive only sees own customers")
neha_customer = next(row for row in all_customers if row["ownerId"] == neha_id)
expect(rahul, "/api/customers/" + neha_customer["id"], 404)
expect(rahul, "/api/customers/" + neha_customer["id"], 404, "DELETE", {})
expect(rahul, "/crm/customers/" + neha_customer["id"], 404)

# ---------------------------------------------------------------- customers
customer_form = {
    "name": "API check " + unique_suffix(), "status": "Active", "ownerId": rahul_id,
    "email": "check-" + unique_suffix() + "@example.test", "phone": unique_mobile()
}
expect(rahul, "/api/customers", 400, "POST", customer_form, send_csrf=False)
expect(rahul, "/api/customers", 400, "POST", {**customer_form, "email": "bad", "phone": "12"})
expect(rahul, "/api/customers", 403, "POST", {**customer_form, "ownerId": neha_id})
not_a_sales_executive = expect(admin, "/api/customers", 400, "POST", {**customer_form, "ownerId": admin_id})
check("Sales Executive" in not_a_sales_executive["error"], "Records can only be assigned to a Sales Executive")
create_form_page = expect(manager, "/crm/customers/create", 200)
check("Rahul Verma" in create_form_page and "Priya Nair</option>" not in create_form_page, "Assign dropdown lists only Sales Executives")
created_customer = expect(rahul, "/api/customers", 201, "POST", customer_form)
customer_id = created_customer["id"]
check(created_customer["code"].startswith("CUS-"), "Customer code is generated")
expect(rahul, "/api/customers", 409, "POST", customer_form)
expect(rahul, "/api/customers", 409, "POST", {**customer_form, "email": "different@example.test"})
expect(rahul, "/api/customers/" + customer_id, 200, "PUT", {**customer_form, "name": "Updated check"})
expect(rahul, "/crm/customers/" + customer_id, 200)
search_result = expect(rahul, "/api/customers?q=updated%20check", 200)
check(any(row["id"] == customer_id for row in search_result["items"]), "Search is case insensitive")

# ---------------------------------------------------------------- leads and conversion
lead_form = {**customer_form, "name": "Test lead", "status": "New", "expectedValue": 20000, "priority": "High", "company": "Check Traders"}
expect(rahul, "/api/leads", 400, "POST", {**lead_form, "status": "Qualified"})
created_lead = expect(rahul, "/api/leads", 201, "POST", lead_form)
lead_id = created_lead["id"]
expect(rahul, "/api/leads/" + lead_id, 400, "PUT", {**lead_form, "status": "Converted"})
expect(rahul, "/api/leads/" + lead_id, 400, "PUT", {**lead_form, "status": "Qualified"})
expect(rahul, "/api/leads/" + lead_id, 200, "PUT", {**lead_form, "status": "Contacted"})
edit_page = expect(rahul, "/crm/leads/" + lead_id + "/edit", 200)
check('value="Qualified"' in edit_page and 'value="New"' not in edit_page, "Lead form only offers allowed next statuses")
expect(rahul, "/api/leads/" + lead_id + "/convert", 409, "POST", {})
expect(rahul, "/api/leads/" + lead_id, 200, "PUT", {**lead_form, "status": "Qualified"})
conversion = expect(rahul, "/api/leads/" + lead_id + "/convert", 200, "POST", {})
check(conversion["customerId"] == customer_id, "Lead conversion reuses the matching customer")
expect(rahul, "/api/leads/" + lead_id + "/convert", 409, "POST", {})
expect(rahul, "/api/leads/" + lead_id, 409, "PUT", {**lead_form, "status": "Lost"})
created_deals = [row for row in expect(rahul, "/api/opportunities?q=sales%20enquiry", 200)["items"] if row["leadId"] == lead_id]
check(len(created_deals) == 1 and created_deals[0]["amount"] == 20000, "Conversion creates an opportunity with the expected value")

# ---------------------------------------------------------------- opportunities
deal_form = {"name": "Test opportunity", "status": "Open", "ownerId": rahul_id, "customerId": customer_id,
             "stage": "Proposal", "amount": 1000, "probability": 50, "closeDate": tomorrow}
for bad_change in [{"amount": 0}, {"amount": -1}, {"amount": 10.555}, {"probability": 101}, {"probability": -1},
                   {"closeDate": yesterday}, {"customerId": neha_customer["id"]}, {"stage": "Invalid"}, {"status": "Won"},
                   {"stage": "Won", "status": "Won", "probability": 80}]:
    expected_status = 404 if "customerId" in bad_change else 400
    expect(rahul, "/api/opportunities", expected_status, "POST", {**deal_form, **bad_change})
created_deal = expect(rahul, "/api/opportunities", 201, "POST", deal_form)
won_deal = expect(rahul, "/api/opportunities/" + created_deal["id"], 200, "PUT", {**deal_form, "stage": "Won", "status": "Won", "probability": 100})
check(won_deal["outcomeDate"] == str(india_today), "Winning a deal records today's outcome date")

pipeline = expect(rahul, "/api/reports/pipeline", 200)
check(all(owner["ownerId"] == rahul_id for owner in pipeline["owners"]), "Pipeline API is scoped to the sales executive")

# ---------------------------------------------------------------- follow-ups, activities and Mark done
follow_up_form = {"name": "Test call", "status": "Planned", "ownerId": rahul_id, "customerId": customer_id, "type": "Call", "dueDate": tomorrow}
expect(rahul, "/api/followups", 400, "POST", {**follow_up_form, "dueDate": yesterday})
expect(rahul, "/api/followups", 400, "POST", {**follow_up_form, "type": "Bad"})
expect(rahul, "/api/followups", 400, "POST", {**follow_up_form, "leadId": lead_id})
expect(rahul, "/api/followups", 400, "POST", {**follow_up_form, "status": "Missed"})
created_follow_up = expect(rahul, "/api/followups", 201, "POST", follow_up_form)
follow_up_id = created_follow_up["id"]
rescheduled = expect(rahul, "/api/followups/" + follow_up_id, 200, "PUT", {**follow_up_form, "dueDate": str(india_today + datetime.timedelta(days=3))})
check(rescheduled["dueDate"] == str(india_today + datetime.timedelta(days=3)), "Follow-up can be rescheduled")
completed = expect(rahul, "/api/followups/" + follow_up_id + "/complete", 200, "POST", {})
check(completed["status"] == "Completed", "API Mark done completes the follow-up")
expect(rahul, "/api/followups/" + follow_up_id + "/complete", 409, "POST", {})

second_follow_up = expect(rahul, "/api/followups", 201, "POST", {**follow_up_form, "name": "Browser done check"})
expect(rahul, "/crm/followups/" + second_follow_up["id"] + "/complete", 400, "POST", {"returnUrl": "/"}, as_form=True, send_csrf=False)
dashboard_after_done = expect(rahul, "/crm/followups/" + second_follow_up["id"] + "/complete", 200, "POST", {"returnUrl": "/crm/followups"}, as_form=True)
check("Marked as done." in dashboard_after_done, "Mark done button works and returns to the list")
check(expect(rahul, "/api/followups/" + second_follow_up["id"], 200)["status"] == "Completed", "Mark done saved the new status")
offsite_redirect = expect(rahul, "/crm/followups/" + second_follow_up["id"] + "/complete", 200, "POST", {"returnUrl": "https://example.com"}, as_form=True)
check("Only a planned item can be marked as done." in offsite_redirect, "Mark done ignores off-site return URLs")
expect(neha, "/crm/followups/" + second_follow_up["id"] + "/complete", 404, "POST", {"returnUrl": "/"}, as_form=True)
expect(rahul, "/api/customers/" + customer_id + "/complete", 404, "POST", {})

# Completing a follow-up on a New lead moves the lead to Contacted (PDF section 8, step 5).
new_lead = expect(rahul, "/api/leads", 201, "POST", {**lead_form, "name": "Follow-up lead", "email": "fl-" + unique_suffix() + "@example.test", "phone": unique_mobile()})
lead_follow_up = expect(rahul, "/api/followups", 201, "POST", {**follow_up_form, "customerId": None, "leadId": new_lead["id"], "name": "Intro call"})
expect(rahul, "/api/followups/" + lead_follow_up["id"] + "/complete", 200, "POST", {})
check(expect(rahul, "/api/leads/" + new_lead["id"], 200)["status"] == "Contacted", "Completed follow-up updates the related New lead to Contacted")

created_activity = expect(rahul, "/api/activities", 201, "POST", {**follow_up_form, "status": "Pending", "type": "Meeting", "dueDate": yesterday})
expect(rahul, "/api/activities/" + created_activity["id"] + "/complete", 200, "POST", {})
history = [row for row in expect(admin, "/Reports?report=audit&export=true&q=" + created_activity["id"], 200).split("\r\n") if "Status Change" in row]
check(len(history) == 1, "Mark done is recorded in the audit log")

details_page = expect(rahul, "/crm/customers/" + customer_id, 200)
check("Test call" in details_page and "Opportunities" in details_page and "History" in details_page, "Customer page shows related records and history")
prefilled_form = expect(rahul, "/crm/followups/create?customerId=" + customer_id, 200)
check(re.search(r'<option selected="selected" value="' + customer_id + '"', prefilled_form) is not None, "Schedule follow-up link preselects the customer")
check('data-record-owners=' in prefilled_form and customer_id in prefilled_form, "Form knows each related record's owner for the Assigned to auto-select")
opportunity_form_page = expect(manager, "/crm/opportunities/create", 200)
check('data-val-required="Select a customer."' in opportunity_form_page, "Opportunity form requires a customer on the client side")
expect(rahul, "/api/customers/" + customer_id, 409, "DELETE", {})

# ---------------------------------------------------------------- reports
for report_name in ["customers", "leads", "followups", "opportunities", "pipeline", "conversion", "user-activity", "audit"]:
    expect(admin, "/Reports?report=" + report_name, 200)
expect(admin, "/Reports?report=unknown", 404)
expect(manager, "/Reports?report=user-activity", 200)
expect(manager, "/Reports?report=audit", 403)
expect(rahul, "/Reports?report=user-activity", 403)
expect(rahul, "/Reports?report=pipeline", 200)
csv_text = expect(admin, "/Reports?report=customers&export=true&q=Updated", 200)
check("Updated check" in csv_text and neha_customer["name"] not in csv_text, "Filtered CSV export")
audit_csv = expect(admin, "/Reports?report=audit&export=true", 200)
check("Convert" in audit_csv and "PasswordHash" not in audit_csv and crm_client.DEMO_PASSWORD not in audit_csv, "Audit entries exclude secrets")
manager_activity_csv = expect(manager, "/Reports?report=user-activity&export=true", 200)
check("Total actions" in manager_activity_csv, "Manager can export the user activity report")

lead_report_csv = expect(rahul, "/Reports?report=leads&export=true", 200)
check("Converted to customer" in lead_report_csv and "Updated check" in lead_report_csv, "Lead report shows conversion information")
pipeline_csv = expect(manager, "/Reports?report=pipeline&export=true", 200)
check("Stage-wise" in pipeline_csv and "Owner-wise" in pipeline_csv and "Weighted value" in pipeline_csv, "Pipeline report is stage-wise, owner-wise and weighted")

search_page = expect(rahul, "/Search?q=karthik", 200)
check("Karthik Rao" in search_page, "Global search finds own records")
other_search = expect(rahul, "/Search?q=" + neha_customer["name"].split()[0], 200)
check(neha_customer["name"] not in other_search, "Global search hides other owners' records")
check("No matches" in expect(rahul, "/Search?q=zzzz-nothing", 200), "Global search shows an empty state")
for sort_name in ["name", "email", "newest", "oldest"]:
    expect(admin, "/Users?sort=" + sort_name, 200)

team_page = expect(manager, "/Team?period=month", 200)
check("Rahul Verma" in team_page and "Neha Iyer" in team_page, "Team page lists every sales executive")

# ---------------------------------------------------------------- clean up test records
for module_name, record_id in [("followups", follow_up_id), ("followups", second_follow_up["id"]), ("followups", lead_follow_up["id"]), ("leads", new_lead["id"]),
                               ("activities", created_activity["id"]), ("opportunities", created_deal["id"])]:
    expect(rahul, "/api/" + module_name + "/" + record_id, 200, "DELETE", {})
for deal in created_deals:
    expect(rahul, "/api/opportunities/" + deal["id"], 200, "DELETE", {})
expect(rahul, "/api/leads/" + lead_id, 200, "DELETE", {})
expect(rahul, "/api/customers/" + customer_id, 200, "DELETE", {})
expect(rahul, "/api/customers/" + customer_id, 404)

expect(rahul, "/api/auth/logout", 200, "POST", {})
expect(rahul, "/api/customers", 401)
check("password" not in json.dumps(all_customers).lower(), "DTOs exclude password fields")

print(f"\n{crm_client.passed_checks} checks passed.")
