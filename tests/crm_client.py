"""Small HTTP client shared by the acceptance scripts.

It keeps cookies like a browser, sends the anti-forgery token and counts passed checks.
Only the Python standard library is used, so no pip install is needed.
"""
import http.cookiejar
import json
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5080"
DEMO_PASSWORD = "Acxiom@123"

passed_checks = 0


def check(condition, check_name, failure_detail=""):
    """Stops the run at the first failure, so the problem is easy to see."""
    global passed_checks
    if not condition:
        raise AssertionError(check_name + ": " + str(failure_detail)[:900])
    passed_checks += 1
    print("PASS", check_name)


class CrmClient:
    def __init__(self):
        self.cookie_jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookie_jar))
        self.csrf_token = ""

    def call(self, path, method="GET", body=None, send_csrf=True, as_form=False):
        headers = {}
        request_body = None
        if body is not None:
            if as_form:
                form_fields = dict(body)
                if send_csrf:
                    form_fields["__RequestVerificationToken"] = self.csrf_token
                request_body = urllib.parse.urlencode(form_fields).encode()
                headers["Content-Type"] = "application/x-www-form-urlencoded"
            else:
                request_body = json.dumps(body).encode()
                headers["Content-Type"] = "application/json"
        if send_csrf:
            headers["X-CSRF-TOKEN"] = self.csrf_token

        request = urllib.request.Request(BASE_URL + path, data=request_body, headers=headers, method=method)
        try:
            response = self.opener.open(request)
        except urllib.error.HTTPError as http_error:
            response = http_error
        response_text = response.read().decode("utf-8-sig")
        try:
            response_value = json.loads(response_text)
        except ValueError:
            response_value = response_text
        return response.status, response_value

    def refresh_csrf(self):
        self.csrf_token = self.call("/api/auth/csrf")[1]["requestToken"]

    def login(self, email, password=DEMO_PASSWORD):
        self.refresh_csrf()
        status_code, response_value = self.call("/api/auth/login", "POST", {"email": email, "password": password})
        check(status_code == 200, "Login " + email, (status_code, response_value))
        # The token must be re-issued after login because it is tied to the signed-in user.
        self.refresh_csrf()
        return response_value["id"]


def expect(client, path, expected_status, method="GET", body=None, **options):
    """Calls the URL and checks the HTTP status. Returns the parsed response."""
    actual_status, response_value = client.call(path, method, body, **options)
    check(actual_status == expected_status, f"{method} {path} → {expected_status}", (actual_status, response_value))
    return response_value
