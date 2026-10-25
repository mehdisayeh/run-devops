"""Run against isolated local apps on 5079/5090; see ACCOUNTS.md.

Creates synthetic accounts/orders only in the supplied disposable smoke database.
Uses mongosh in velora-db for admin promotion and session-expiry test fixtures.
"""
import hashlib
import http.cookiejar
import json
import os
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request

database = os.environ.get("VELORA_ACCOUNT_SMOKE_DATABASE", "")
assert re.fullmatch(r"velora_accounts_smoke_[a-z0-9]+", database), "Supply an isolated smoke database name."
api, client = "http://localhost:5079", "http://localhost:5090"
password = "A long smoke test passphrase"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def browser():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), NoRedirect())


def call(path, body=None, token=None, method=None, web=None, form=False):
    headers = {}
    if body is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded" if form else "application/json"
        body = urllib.parse.urlencode(body).encode() if form else json.dumps(body).encode()
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request((client if web else api) + path, data=body, headers=headers, method=method)
    try:
        with (web or urllib.request.build_opener()).open(request, timeout=20) as response:
            return response.status, response.read().decode(), response.headers
    except urllib.error.HTTPError as response:
        return response.code, response.read().decode(), response.headers


def json_call(path, body=None, token=None, method=None, expected=200):
    status, text, _ = call(path, body, token, method)
    assert status == expected, (path, status, text[:500])
    return json.loads(text) if text else None


def csrf(path, web):
    status, html, _ = call(path, web=web)
    assert status == 200, (path, status, html[:300])
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html).group(1)


def mongo(js):
    subprocess.run(["docker", "exec", "velora-db", "mongosh", "--quiet", "--eval",
                    f"const smoke = db.getSiblingDB('{database}'); {js}"], check=True, capture_output=True)


alice = json_call("/api/account/register", {"fullName": "Alice", "email": "alice@example.test", "password": password, "roles": ["Admin"]})
bob = json_call("/api/account/register", {"fullName": "Bob", "email": "bob@example.test", "password": password})
assert alice["roles"] == ["Customer"] and "passwordHash" not in alice
json_call("/api/account/register", {"fullName": "Duplicate", "email": "ALICE@example.test", "password": password}, expected=409)
session_a = json_call("/api/account/login", {"email": "ALICE@example.test", "password": password})["accessToken"]
session_b = json_call("/api/account/login", {"email": "bob@example.test", "password": password})["accessToken"]
assert call("/api/account/me")[0] == 401
assert call("/api/account/customers", token=session_a)[0] == 403
address = {"fullName": "Alice", "email": "alice@example.test", "line1": "1 Test Street", "city": "Paris", "postalCode": "75001", "country": "France"}
saved = json_call("/api/account/addresses", address, session_a)
json_call("/api/account/addresses/" + saved["id"], address, session_b, "PUT", expected=404)
json_call("/api/account/addresses/" + saved["id"], token=session_b, method="DELETE", expected=404)
assert json_call("/api/account/me", token=session_b)["addresses"] == []
profile = json_call("/api/account/me", {"fullName": "Updated Alice", "id": bob["id"], "roles": ["Admin"]}, session_a, "PUT")
assert profile["id"] == alice["id"] and profile["roles"] == ["Customer"]
receipt = json_call("/api/orders", {"items": [{"productId": 1, "quantity": 1}], "address": address}, session_a)
assert call("/api/orders/" + receipt["id"])[0] == 404
assert call("/api/orders/" + receipt["id"], token=session_b)[0] == 404
assert json_call("/api/orders/" + receipt["id"], token=session_a)["id"] == receipt["id"]
assert len(json_call("/api/orders/mine", token=session_a)) == 1
assert json_call("/api/orders/mine", token=session_b) == []
assert call("/api/orders/mine")[0] == 401
assert call("/api/orders", {"items": [{"productId": 1, "quantity": 1}], "address": address}, token="0" * 64)[0] == 401

web = browser()
assert call("/Account/Profile", web=web)[0] == 302
assert call("/Account/Login", {"Email": alice["email"], "Password": password}, method="POST", web=web, form=True)[0] == 400
status, _, headers = call("/Account/Login", {"Email": alice["email"], "Password": password,
    "returnUrl": "https://example.test/unsafe", "__RequestVerificationToken": csrf("/Account/Login", web)}, web=web, form=True)
assert status == 302 and headers["Location"] == "/Account/Profile"
cookie = " ".join(headers.get_all("Set-Cookie", [])).lower()
assert "httponly" in cookie and "samesite=lax" in cookie
assert call("/Account/Profile", web=web)[0] == 200
form_address = {"FullName": "Alice", "Email": "alice@example.test", "Line1": "2 Form Street", "City": "Paris", "PostalCode": "75002", "Country": "France"}
assert call("/Account/SaveAddress", {**form_address, "__RequestVerificationToken": csrf("/Account/Address", web)}, web=web, form=True)[0] == 302
form_saved = next(a for a in json_call("/api/account/me", token=session_a)["addresses"] if a["id"] != saved["id"])
assert call("/Account/SaveAddress", {**form_address, "City": "Lyon", "id": form_saved["id"],
    "__RequestVerificationToken": csrf("/Account/Address?id=" + form_saved["id"], web)}, web=web, form=True)[0] == 302
assert next(a for a in json_call("/api/account/me", token=session_a)["addresses"] if a["id"] == form_saved["id"])["address"]["city"] == "Lyon"
assert call("/Account/DeleteAddress", {"id": form_saved["id"], "__RequestVerificationToken": csrf("/Account/Profile", web)}, web=web, form=True)[0] == 302
assert len(json_call("/api/account/me", token=session_a)["addresses"]) == 1
assert call("/Account/UpdateProfile", {"FullName": "Alice Client", "__RequestVerificationToken": csrf("/Account/Profile", web)}, web=web, form=True)[0] == 302
assert json_call("/api/account/me", token=session_a)["fullName"] == "Alice Client"
assert receipt["id"] in call("/Account/Orders", web=web)[1]
assert "1 Test Street" in call("/Checkout", web=web)[1]
assert call("/Account/Admin", web=web)[0] == 302

assert re.fullmatch(r"[a-f0-9]{32}", alice["id"])
mongo(f"smoke.Customers.updateOne({{_id:'{alice['id']}'}},{{$set:{{Roles:['Customer','Admin']}}}})")
assert len(json_call("/api/account/customers", token=session_a)) == 2
assert call("/Account/Admin", web=web)[0] == 200
mongo(f"smoke.Customers.updateOne({{_id:'{alice['id']}'}},{{$set:{{Roles:['Customer']}}}})")
assert call("/api/account/customers", token=session_a)[0] == 403
assert call("/Account/Admin", web=web)[0] == 302
assert call("/Account/Logout", {}, web=web, form=True)[0] == 400
assert call("/Account/Logout", {"__RequestVerificationToken": csrf("/Account/Profile", web)}, web=web, form=True)[0] == 302
assert call("/Account/Profile", web=web)[0] == 302
registration_web = browser()
status, invalid_html, _ = call("/Account/Register", {"FullName": "Carol", "Email": "carol@example.test", "Password": "short",
    "__RequestVerificationToken": csrf("/Account/Register", registration_web)}, web=registration_web, form=True)
assert status == 200 and 'validation-summary-errors' in invalid_html and 'value="short"' not in invalid_html
assert call("/Account/Register", {"FullName": "Carol", "Email": "carol@example.test", "Password": password,
    "__RequestVerificationToken": csrf("/Account/Register", registration_web)}, web=registration_web, form=True)[0] == 302
assert call("/Account/Profile", web=registration_web)[0] == 302
json_call("/api/account/logout", {}, session_a)
assert call("/api/account/me", token=session_a)[0] == 401
token_hash = hashlib.sha256(session_b.encode()).hexdigest()
mongo(f"smoke.CustomerSessions.updateOne({{_id:'{token_hash}'}},{{$set:{{ExpiresAtUtc:new Date(0)}}}})")
assert call("/api/account/me", token=session_b)[0] == 401
print("PASS: registration, role injection, duplicate email, sessions, profile/address isolation, order ownership, MVC sign-in, CSRF, local redirects, admin role changes, logout and expiry.")
