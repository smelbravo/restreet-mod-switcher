"""Announce newly visible versions of ReStreet mods in Discord."""

import json
import os
import re
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


COMMUNITY = "https://thunderstore.io/c/restreet/"
CATALOGUE = COMMUNITY + "api/v1/package/"
STATE = Path(".thunderstore-feed-state.json")
SEGMENT = re.compile(r"[A-Za-z0-9_]+")
VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.+-]+)?")
WEBHOOK_URL = re.compile(
    r"https://discord\.com/api(?:/v[0-9]+)?/webhooks/[0-9]{17,22}/[A-Za-z0-9._-]{40,160}"
)


def read_catalogue():
    request = Request(CATALOGUE, headers={
        "Accept": "application/json",
        "User-Agent": "ReStreet-Mod-Switcher-Discord-Feed",
    })
    with urlopen(request, timeout=30) as response:
        packages = json.load(response)
    if not isinstance(packages, list):
        raise ValueError("Expected a list of ReStreet packages.")

    releases = []
    for package in packages:
        owner, name = package["owner"], package["name"]
        if not SEGMENT.fullmatch(owner) or not SEGMENT.fullmatch(name):
            raise ValueError("Unexpected Thunderstore package identifier.")
        for version in package["versions"]:
            number = version["version_number"]
            if not VERSION.fullmatch(number):
                raise ValueError("Unexpected Thunderstore version number.")
            releases.append((version["date_created"], owner, name, number))
    return sorted(releases)


def post(webhook, **message):
    payload = {"allowed_mentions": {"parse": []}, **message}
    request = Request(webhook + "?wait=true", data=json.dumps(payload).encode(), headers={
        "Content-Type": "application/json",
        "User-Agent": "DiscordBot (https://github.com/smelbravo/restreet-mod-switcher, 1.0)",
    })
    try:
        with urlopen(request, timeout=30) as response:
            if not json.load(response).get("id"):
                raise ValueError("Discord did not confirm message delivery.")
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
        for _, owner, name, number in releases:
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
        post(webhook, content="Thunderstore · ReStreet · Teste do feed / Feed test / Prueba del feed")
        return

    posted = 0
    for _, owner, name, number in releases:
        package = f"{owner}/{name}"
        announced = state.setdefault(package, set())
        if number in announced:
            continue
        label = "Mod atualizado / Mod updated / Mod actualizado" if announced else (
            "Novo mod / New mod / Nuevo mod"
        )
        post(webhook, embeds=[{
            "author": {"name": label},
            "title": f"{name.replace('_', ' ')} · {number}"[:256],
            "url": f"{COMMUNITY}p/{package}/",
            "footer": {"text": f"Thunderstore · {owner}"[:256]},
            "color": 15844367,
        }])
        announced.add(number)
        save_state(state)
        posted += 1
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
