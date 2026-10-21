"""Repeatable local LR04 HTTP evidence. Credentials and cookies are never recorded."""
import argparse
from datetime import datetime, timezone
import getpass
import http.cookiejar
import json
import os
from pathlib import Path
import sys
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
BASE = "http://localhost:5080"
ALICE = "20000000-0000-0000-0000-000000000001"
BOB = "20000000-0000-0000-0000-000000000002"
INTERNAL = "30000000-0000-0000-0000-000000000003"
FOREIGN_COMMENT = "30000000-0000-0000-0000-000000000002"
MISSING = "99999999-9999-9999-9999-999999999999"


class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, method, path, data=None):
        encoded = None if data is None else json.dumps(data).encode("utf-8")
        headers = {"Accept": "application/json"}
        if encoded is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(BASE + path, data=encoded,
            headers=headers, method=method)
        try:
            response = self.opener.open(request, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            status = response.status
            content_type = response.headers.get("Content-Type", "")
            raw = response.read()
        if not raw:
            body = None
        else:
            try:
                body = json.loads(raw)
            except (json.JSONDecodeError, UnicodeDecodeError):
                body = {"non_json_body_omitted": True}
        return {"status": status, "content_type": content_type, "body": body}

    def login(self, name, password):
        result = self.request("POST", "/api/auth/login",
            {"userName": name, "password": password})
        if result["status"] != 204:
            raise RuntimeError("Login failed for " + name + ": status="
                + str(result["status"]) + ". Check seed password or login quota.")
        return self


class Evidence:
    def __init__(self, phase):
        self.data = {"phase": phase, "utc": datetime.now(timezone.utc).isoformat(),
            "base_url": BASE, "checks": []}

    def check(self, scenario, passed, **extra):
        self.data["checks"].append({"scenario": scenario, "passed": bool(passed), **extra})
        print(("PASS " if passed else "FAIL ") + scenario)

    def call(self, scenario, client, method, path, expected, data=None):
        result = client.request(method, path, data)
        self.check(scenario, result["status"] == expected,
            method=method, path=path, expected=expected, response=result)
        return result

    def save(self):
        self.data["passed"] = bool(self.data["checks"]) and all(
            item["passed"] for item in self.data["checks"])
        if "error" in self.data:
            self.data["passed"] = False
        directory = ROOT / "docs/evidence/lab-04"
        directory.mkdir(parents=True, exist_ok=True)
        target = directory / (self.data["phase"] + ".json")
        target.write_text(json.dumps(self.data, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
        print("Evidence: " + str(target.relative_to(ROOT)))
        print("RESULT: " + ("PASS" if self.data["passed"] else "FAIL"))
        return 0 if self.data["passed"] else 1


def before(evidence, alice, bob):
    path = "/api/incidents/" + BOB
    evidence.call("before-owner-read", alice, "GET", "/api/incidents/" + ALICE, 200)
    foreign = evidence.call("before-Alice-reads-Bob", alice, "GET", path, 200)
    if foreign["status"] != 200:
        return
    original = evidence.call("before-Bob-state", bob, "GET", path, 200)
    if original["status"] != 200:
        return
    body = original["body"]
    mutation = {"title": "LR04-controlled-" + uuid.uuid4().hex,
        "description": "Контрольований локальний PoC: Alice змінює текст інциденту Bob до виправлення авторизації."}
    try:
        evidence.call("before-Alice-updates-Bob", alice, "PUT", path, 204, mutation)
        changed = evidence.call("before-Bob-state-after-update", bob, "GET", path, 200)
        evidence.check("before-mutation-is-visible",
            changed["status"] == 200
            and changed["body"].get("title") == mutation["title"]
            and changed["body"].get("description") == mutation["description"]
            and body["title"] != changed["body"]["title"])
    finally:
        evidence.call("before-restore-Bob-content", bob, "PUT", path, 204,
            {"title": body["title"], "description": body["description"]})
        restored = evidence.call("before-Bob-restored", bob, "GET", path, 200)
        evidence.check("before-restoration-confirmed", restored["body"] == body)


def stable(body):
    if not isinstance(body, dict):
        return body
    return {key: value for key, value in body.items()
        if key not in {"traceId", "requestId"}}


def fixed(evidence, alice, bob, morgan):
    path = "/api/incidents/" + BOB
    evidence.call("A01-anonymous", Client(), "GET", "/api/incidents/" + ALICE, 401)
    own = evidence.call("A02-owner-read", alice, "GET", "/api/incidents/" + ALICE, 200)
    denied = evidence.call("A03-foreign-read", alice, "GET", path, 404)
    missing = evidence.call("A10-missing-read", alice, "GET", "/api/incidents/" + MISSING, 404)
    evidence.check("A10-same-404-contract",
        denied["content_type"] == missing["content_type"]
        and stable(denied["body"]) == stable(missing["body"]))
    analyst = evidence.call("A03R-analyst-read", morgan, "GET", "/api/incidents/" + ALICE, 200)
    original = evidence.call("A04-Bob-before", bob, "GET", path, 200)
    evidence.call("A04-foreign-update", alice, "PUT", path, 404,
        {"title": "forbidden", "description": "Заборонена зміна чужого інциденту не повинна впливати на його збережений стан."})
    after_update = evidence.call("A04-Bob-after-update", bob, "GET", path, 200)
    evidence.check("A04-update-does-not-change-state", original["body"] == after_update["body"])
    evidence.call("A04-foreign-delete", alice, "DELETE", path, 404)
    after_delete = evidence.call("A04-Bob-after-delete", bob, "GET", path, 200)
    evidence.check("A04-delete-does-not-change-state", original["body"] == after_delete["body"])
    listing = evidence.call("A05-list", alice, "GET", "/api/incidents", 200)
    items = listing["body"] if isinstance(listing["body"], list) else []
    ids = {item["id"] for item in items}
    evidence.check("A05-known-foreign-records-absent", ALICE in ids and BOB not in ids
        and "20000000-0000-0000-0000-000000000004" not in ids)
    triage = evidence.call("A06-reporter-triage-denied", alice, "POST",
        "/api/incidents/" + ALICE + "/triage", 403)
    unchanged = evidence.call("A06-owner-after-denial", alice, "GET", "/api/incidents/" + ALICE, 200)
    evidence.check("A06-state-unchanged", own["body"] == unchanged["body"])
    evidence.data["audit_trace_id"] = (triage["body"] or {}).get("traceId")
    print("A09 response traceId=" + str(evidence.data["audit_trace_id"]))
    evidence.call("A12-foreign-comment", alice, "GET", "/api/incident-comments/" + FOREIGN_COMMENT, 404)
    evidence.call("A12-internal-denied", alice, "GET", "/api/incident-comments/" + INTERNAL, 404)
    evidence.call("A12-internal-analyst", morgan, "GET", "/api/incident-comments/" + INTERNAL, 200)
    evidence.call("A12-public-Bob-comment-analyst", morgan, "GET",
        "/api/incident-comments/" + FOREIGN_COMMENT, 200)
    reporter_comments = {item["id"] for item in (own["body"] or {}).get("comments", [])}
    analyst_comments = {item["id"] for item in (analyst["body"] or {}).get("comments", [])}
    evidence.check("A12-details-internal-visibility",
        INTERNAL not in reporter_comments and INTERNAL in analyst_comments)

    created = evidence.call("owner-create-fixture", alice, "POST", "/api/incidents", 201,
        {"title": "LR04-http-" + uuid.uuid4().hex,
         "description": "Власний тестовий інцидент для HTTP-перевірки оновлення й overposting.",
         "severity": "Low", "occurredAtUtc": datetime.now(timezone.utc).isoformat()})
    if created["status"] != 201:
        return
    fixture = "/api/incidents/" + created["body"]["id"]
    removed = False
    try:
        initial = evidence.call("A08b-fixture-before", alice, "GET", fixture, 200)
        evidence.call("A08b-forged-fields-rejected", alice, "PUT", fixture, 400,
            {"title": "forged", "description": "forged", "role": "Analyst",
             "ownerUserId": "10000000-0000-0000-0000-000000000002", "status": "Triaged"})
        after = evidence.call("A08b-fixture-after", alice, "GET", fixture, 200)
        evidence.check("A08b-public-state-unchanged", initial["body"] == after["body"])
        title = "LR04-owner-edit-" + uuid.uuid4().hex
        evidence.call("owner-update-allowed", alice, "PUT", fixture, 204,
            {"title": title, "description": "Дозволене редагування власного тексту."})
        edited = evidence.call("owner-update-reloaded", alice, "GET", fixture, 200)
        evidence.check("owner-update-persisted", (edited["body"] or {}).get("title") == title)
        deleted = evidence.call("owner-delete-New-allowed", alice, "DELETE", fixture, 204)
        removed = deleted["status"] == 204
        evidence.call("owner-delete-confirmed", alice, "GET", fixture, 404)
    finally:
        if not removed:
            cleanup = alice.request("DELETE", fixture)
            evidence.check("fixture-cleanup", cleanup["status"] in {204, 404})
    evidence.data["automated_only"] = [
        "A07 successful transition, history, repeated-transition denial and cleanup",
        "A08b database owner/status/severity and Identity role state",
        "A09 structured audit fields and equality with response trace"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--phase", required=True, choices=["before", "fixed"])
    args = parser.parse_args()
    password = os.environ.get("SeedUsers__Password") or getpass.getpass("Study password: ")
    evidence = Evidence(args.phase)
    try:
        alice = Client().login("alice", password)
        bob = Client().login("bob", password)
        if args.phase == "before":
            before(evidence, alice, bob)
        else:
            morgan = Client().login("morgan", password)
            fixed(evidence, alice, bob, morgan)
    except (urllib.error.URLError, TimeoutError) as error:
        evidence.data["error"] = "API unavailable: " + type(error).__name__
        print(evidence.data["error"])
    except RuntimeError as error:
        evidence.data["error"] = str(error)
        print(evidence.data["error"])
    except Exception as error:
        evidence.data["error"] = "Unexpected response: " + type(error).__name__
        print(evidence.data["error"])
    return evidence.save()


if __name__ == "__main__":
    sys.exit(main())
