"""Announce newly visible versions of ReStreet mods in Discord."""

import json
import os
import re
from datetime import datetime
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urljoin, urlsplit
from urllib.request import Request, urlopen


COMMUNITY = "https://thunderstore.io/c/restreet/"
API = "https://thunderstore.io/api/cyberstorm/"
CATALOGUE = API + "listing/restreet/"
STATE = Path(".thunderstore-feed-state.json")
SEGMENT = re.compile(r"[A-Za-z0-9_]+")
VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.+-]+)?")
WEBHOOK_URL = re.compile(
    r"https://discord\.com/api(?:/v[0-9]+)?/webhooks/[0-9]{17,22}/[A-Za-z0-9._-]{40,160}"
)


def read_json(url):
    request = Request(url, headers={
        "Accept": "application/json",
        "User-Agent": "ReStreet-Mod-Switcher-Discord-Feed",
    })
    with urlopen(request, timeout=30) as response:
        return json.load(response)


def read_catalogue():
    packages = set()
    visited = set()
    endpoint = urlsplit(CATALOGUE)
    page_url = CATALOGUE
    while page_url:
        if urlsplit(page_url)._replace(query="") != endpoint or page_url in visited:
            raise ValueError("Invalid Thunderstore catalogue pagination URL.")
        visited.add(page_url)
        page = read_json(page_url)
        results = page["results"]
        if not isinstance(results, list):
            raise ValueError("Expected a list of ReStreet packages.")
        for package in results:
            owner, name = package["namespace"], package["name"]
            if not SEGMENT.fullmatch(owner) or not SEGMENT.fullmatch(name):
                raise ValueError("Unexpected Thunderstore package identifier.")
            packages.add((owner, name))
        page_url = urljoin(page_url, page["next"]) if page["next"] else None

    releases = []
    for owner, name in sorted(packages):
        versions = read_json(f"{API}package/{owner}/{name}/versions/")
        if not isinstance(versions, list):
            raise ValueError("Expected a list of Thunderstore versions.")
        history = []
        for version in versions:
            number = version["version_number"]
            if not VERSION.fullmatch(number):
                raise ValueError("Unexpected Thunderstore version number.")
            created = datetime.fromisoformat(version["datetime_created"])
            history.append((created, number))
        for index, (created, number) in enumerate(sorted(history)):
            releases.append((created, owner, name, number, index == 0))
    print(f"Thunderstore catalogue: {len(packages)} mod(s), {len(releases)} version(s).")
    return sorted(releases)


def post(webhook, **message):
    payload = {"allowed_mentions": {"parse": []}, **message}
    request = Request(webhook + "?wait=true", data=json.dumps(payload).encode(), headers={
        "Content-Type": "application/json",
        "User-Agent": "DiscordBot (https://github.com/smelbravo/restreet-mod-switcher, 1.0)",
    })
    try:
        with urlopen(request, timeout=30) as response:
            message_id = json.load(response).get("id")
            if not message_id:
                raise ValueError("Discord did not confirm message delivery.")
            return message_id
    except HTTPError as error:
        raise RuntimeError(f"Discord returned HTTP {error.code}.") from None
    except (URLError, TimeoutError):
        raise RuntimeError("Could not reach Discord.") from None


def save_state(state):
    pending = STATE.with_suffix(".tmp")
    pending.write_text(json.dumps(
        {package: sorted(versions) for package, versions in sorted(state.items())}
    ), encoding="utf-8")
    pending.replace(STATE)


def check_feed(webhook, mode):
    releases = read_catalogue()
    if not STATE.exists():
        # Set a baseline so enabling the feed never floods Discord with old releases.
        state = {}
        for _, owner, name, number, _ in releases:
            state.setdefault(f"{owner}/{name}", set()).add(number)
        save_state(state)
        print(f"Initialised the feed with {len(releases)} existing version(s).")
        if mode == "check":
            return
    else:
        state = {
            package: set(versions)
            for package, versions in json.loads(STATE.read_text(encoding="utf-8")).items()
        }

    if mode == "test":
        post(webhook, content="Thunderstore · ReStreet · Feed test / Teste do feed / Prueba del feed")
        return

    posted = 0
    for _, owner, name, number, first_release in releases:
        package = f"{owner}/{name}"
        announced = state.setdefault(package, set())
        if number in announced:
            continue
        label = "New mod / Novo mod / Nuevo mod" if first_release else (
            "Mod updated / Mod atualizado / Mod actualizado"
        )
        message_id = post(webhook, embeds=[{
            "author": {"name": label},
            "title": f"{name.replace('_', ' ')} · {number}"[:256],
            "url": f"{COMMUNITY}p/{package}/",
            "footer": {"text": f"Thunderstore · {owner}"[:256]},
            "color": 15844367,
        }])
        announced.add(number)
        save_state(state)
        posted += 1
        print(f"Announced {package} {number} (Discord message {message_id}).")
    print(f"Posted {posted} new version(s).")


def main():
    webhook = os.environ.get("DISCORD_THUNDERSTORE_WEBHOOK", "")
    if not WEBHOOK_URL.fullmatch(webhook):
        raise SystemExit("Set DISCORD_THUNDERSTORE_WEBHOOK to the Discord channel webhook URL.")
    mode = os.environ.get("FEED_MODE", "check")
    if mode not in ("check", "test"):
        raise SystemExit("Invalid Thunderstore feed mode.")

    before = STATE.read_bytes() if STATE.exists() else None
    try:
        check_feed(webhook, mode)
    finally:
        # Retain deliveries that succeeded even if a later one failed in the same run.
        output = os.environ.get("GITHUB_OUTPUT")
        if output and STATE.exists() and STATE.read_bytes() != before:
            with open(output, "a", encoding="utf-8") as result:
                result.write("state_changed=true\n")


if __name__ == "__main__":
    main()
