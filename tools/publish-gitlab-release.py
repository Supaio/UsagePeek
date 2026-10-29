"""Publish a checked portable EXE using GitLab CI's short-lived job token."""

import hashlib
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parent.parent


def request(url, method="GET", data=None, token=None, missing_ok=False):
    headers = {"User-Agent": "UsagePeek-release"}
    if token:
        headers["JOB-TOKEN"] = token
    if isinstance(data, dict):
        data = json.dumps(data, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    elif data is not None:
        headers["Content-Type"] = "application/octet-stream"
    try:
        with urlopen(Request(url, data=data, headers=headers, method=method), timeout=120) as response:
            return response.read()
    except HTTPError as error:
        if missing_ok and error.code == 404:
            return None
        raise RuntimeError(f"GitLab {method} failed with HTTP {error.code}.") from None


def main():
    check_only = sys.argv[1:] == ["--check"]
    if sys.argv[1:] and not check_only:
        raise RuntimeError("Usage: publish-gitlab-release.py [--check]")

    manifest_bytes = (ROOT / "update.json").read_bytes()
    manifest = json.loads(manifest_bytes)
    version = manifest["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise RuntimeError("Manifest version must contain three numeric components.")
    tag = "v" + version
    project_url = os.environ.get("CI_PROJECT_URL", "https://gitlab.com/Supaio/usagepeek")
    expected_url = f"{project_url}/-/releases/{tag}/downloads/UsagePeek.exe"
    if manifest["url"] != expected_url:
        raise RuntimeError("Manifest must point to this version's GitLab Release EXE.")
    if not check_only and os.environ["CI_COMMIT_TAG"] != tag:
        raise RuntimeError("Tag and update manifest versions do not match.")

    program = (ROOT / "Program.cs").read_text(encoding="utf-8-sig")
    for attribute in ("AssemblyVersion", "AssemblyFileVersion"):
        if f'{attribute}("{version}.0")' not in program:
            raise RuntimeError(f"{attribute} does not match the release version.")
    binary = (ROOT / "dist" / "UsagePeek.exe").read_bytes()
    digest = hashlib.sha256(binary).hexdigest().upper()
    if not binary.startswith(b"MZ") or digest != manifest["sha256"].upper():
        raise RuntimeError("Release EXE does not match the manifest's SHA-256.")
    print(f"Verified {tag}: {digest}")
    if check_only:
        return

    api = os.environ["CI_API_V4_URL"]
    project = os.environ["CI_PROJECT_ID"]
    token = os.environ["CI_JOB_TOKEN"]
    commit = os.environ["CI_COMMIT_SHA"]
    release_url = f"{api}/projects/{project}/releases/{tag}"
    existing = request(release_url, token=token, missing_ok=True)
    if existing and json.loads(existing)["commit"]["id"] != commit:
        raise RuntimeError("An existing release refers to a different commit.")

    files = {
        "UsagePeek.exe": binary,
        "SHA256SUMS.txt": f"{digest}  UsagePeek.exe\n".encode("ascii"),
        "update.json": manifest_bytes,
    }
    links = []
    for name, content in files.items():
        url = f"{api}/projects/{project}/packages/generic/usagepeek/{version}/{name}"
        published = request(url, token=token, missing_ok=True)
        if published is None:
            request(url, method="PUT", data=content, token=token)
        elif published != content:
            raise RuntimeError(f"Existing {name} differs; refusing to overwrite this version.")
        # The updater and testers must be able to download without GitLab credentials.
        if request(url) != content:
            raise RuntimeError(f"Public download verification failed for {name}.")
        links.append({"name": name, "url": url, "direct_asset_path": "/" + name})
        print(f"Uploaded and publicly verified {name}.")

    description = (
        f"UsagePeek {version} Windows 版本。\n\n"
        + manifest["notes"]
        + "\n\n项目思路来源于 [OpenUsage](https://github.com/robinebers/openusage)，"
        + "鲸鱼娘形象鸣谢 **ZipZipPipe**。完整署名见 "
        + f"[THIRD_PARTY_NOTICES.md]({project_url}/-/blob/{tag}/THIRD_PARTY_NOTICES.md)。"
        + f"\n\n**SHA-256:** `{digest}`\n"
    )
    payload = {"name": f"UsagePeek {tag}", "description": description, "assets": {"links": links}}
    if existing:
        # Existing links already target the immutable, checked package files.
        payload.pop("assets")
        request(release_url, method="PUT", data=payload, token=token)
    else:
        payload.update({"tag_name": tag, "ref": commit})
        request(f"{api}/projects/{project}/releases", method="POST", data=payload, token=token)

    for name, content in files.items():
        if request(f"{project_url}/-/releases/{tag}/downloads/{name}") != content:
            raise RuntimeError(f"Release download verification failed for {name}.")
    print(f"Release verified: {project_url}/-/releases/{tag}")


if __name__ == "__main__":
    try:
        main()
    except (KeyError, OSError, RuntimeError, ValueError) as error:
        # Never print request headers, job tokens, or GitLab response bodies.
        print(f"Release failed: {error}", file=sys.stderr)
        sys.exit(1)
