"""Exercise release replacement safety without contacting GitLab."""

import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from contextlib import redirect_stdout
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "publish-gitlab-release.py"
SPEC = importlib.util.spec_from_file_location("usagepeek_release_test_target", SCRIPT)
publisher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publisher)

API = "https://gitlab.example.test/api/v4"
PROJECT = "88265538"
PROJECT_URL = "https://gitlab.example.test/Supaio/usagepeek"
TAG = "v2.0.4"
COMMIT = "b" * 40
OLD_COMMIT = "a" * 40
RELEASE_URL = f"{API}/projects/{PROJECT}/releases/{TAG}"
RELEASES_URL = f"{API}/projects/{PROJECT}/releases"
DOWNLOAD_ROOT = f"{PROJECT_URL}/-/releases/{TAG}/downloads/"
PACKAGE_ROOT = f"{API}/projects/{PROJECT}/packages/generic/usagepeek/"
NEW_PACKAGE_ROOT = PACKAGE_ROOT + "2.0.4-r" + COMMIT[:12] + "/"


def encode(value):
    return json.dumps(value, ensure_ascii=False).encode("utf-8")


class FakeGitLab:
    def __init__(self, manifest, binary):
        self.manifest = manifest
        self.binary = binary
        self.events = []
        self.posts = []
        self.bad_public_file = None
        self.fail_new_post = False
        self.change_on_second_read = False
        self.release_reads = 0
        self.packages = {}
        old_binary = b"MZ previously published executable"
        self.old_digest = hashlib.sha256(old_binary).hexdigest().upper()
        old_manifest = dict(manifest, sha256=self.old_digest, notes="Previous release")
        old_files = {
            "UsagePeek.exe": old_binary,
            "update.json": encode(old_manifest),
            "SHA256SUMS.txt": f"{self.old_digest}  UsagePeek.exe\n".encode("ascii"),
        }
        links = []
        for index, (name, content) in enumerate(old_files.items()):
            url = PACKAGE_ROOT + "2.0.4/" + name
            self.packages[url] = content
            links.append({"id": index + 1, "name": name, "url": url,
                          "direct_asset_url": DOWNLOAD_ROOT + name,
                          "link_type": "other"})
        self.release = {
            "name": "Original UsagePeek 2.0.4",
            "description": "Previously published release description.",
            "tag_name": TAG,
            "created_at": "2026-10-31T15:47:07Z",
            "released_at": "2026-10-31T15:47:07Z",
            "commit": {"id": OLD_COMMIT},
            "assets": {"links": links},
        }
        self.original_release = copy.deepcopy(self.release)

    def request(self, url, method="GET", data=None, token=None, missing_ok=False):
        self.events.append((method, url, token is not None))
        if url == RELEASE_URL:
            if method == "GET":
                self.release_reads += 1
                if self.change_on_second_read and self.release_reads == 2:
                    self.release["created_at"] = "2026-11-01T01:00:00Z"
                if self.release is None:
                    if missing_ok:
                        return None
                    raise RuntimeError("Release not found.")
                return encode(self.release)
            if method == "DELETE":
                self.release = None
                return b""
            if method == "PUT":
                self.release.update(copy.deepcopy(data))
                return encode(self.release)

        if url == RELEASES_URL and method == "POST":
            self.posts.append(copy.deepcopy(data))
            if self.fail_new_post:
                self.fail_new_post = False
                raise RuntimeError("Simulated release creation failure.")
            if self.release is not None:
                raise RuntimeError("Release already exists.")
            self.release = copy.deepcopy(data)
            self.release["created_at"] = "2026-11-01T02:00:00Z"
            self.release["commit"] = {"id": COMMIT}
            for link in self.release.get("assets", {}).get("links", []):
                link["direct_asset_url"] = DOWNLOAD_ROOT + link["name"]
            return encode(self.release)

        if url.startswith(PACKAGE_ROOT):
            if method == "PUT":
                self.packages[url] = data
                return b"{}"
            if method == "GET":
                if token is None and url == self.bad_public_file:
                    return b"corrupt public download"
                if url in self.packages:
                    return self.packages[url]
                if missing_ok:
                    return None
                raise RuntimeError("Package file not found.")

        if url.startswith(DOWNLOAD_ROOT) and method == "GET":
            if self.release is None:
                raise RuntimeError("Release not found.")
            name = url[len(DOWNLOAD_ROOT):]
            link = next(item for item in self.release["assets"]["links"]
                        if item["name"] == name)
            return self.packages[link["url"]]
        raise AssertionError(f"Unexpected mocked request: {method} {url}")

    def install_current_release(self):
        files = {
            "UsagePeek.exe": self.binary,
            "update.json": encode(self.manifest),
            "SHA256SUMS.txt": f"{self.manifest['sha256']}  UsagePeek.exe\n".encode("ascii"),
        }
        for link in self.release["assets"]["links"]:
            link["url"] = NEW_PACKAGE_ROOT + link["name"]
        for name, content in files.items():
            self.packages[NEW_PACKAGE_ROOT + name] = content
        self.release["commit"]["id"] = COMMIT


class ReleaseReplacementTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="usagepeek-release-test-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "dist").mkdir()
        self.binary = b"MZ newly verified executable"
        self.manifest = {
            "version": "2.0.4",
            "url": DOWNLOAD_ROOT + "UsagePeek.exe",
            "sha256": hashlib.sha256(self.binary).hexdigest().upper(),
            "notes": "Compact integrated quota capsule.",
        }
        (self.root / "update.json").write_bytes(encode(self.manifest))
        (self.root / "dist" / "UsagePeek.exe").write_bytes(self.binary)
        (self.root / "Program.cs").write_text(
            '[assembly: AssemblyVersion("2.0.4.0")]\n'
            '[assembly: AssemblyFileVersion("2.0.4.0")]\n', encoding="utf-8")
        self.server = FakeGitLab(self.manifest, self.binary)

    def run_publisher(self, replace=False, source="api", guard=None):
        environment = {
            "CI_API_V4_URL": API,
            "CI_PROJECT_ID": PROJECT,
            "CI_PROJECT_URL": PROJECT_URL,
            "CI_JOB_TOKEN": "YOUR_TOKEN_HERE",
            "CI_COMMIT_TAG": TAG,
            "CI_COMMIT_SHA": COMMIT,
            "CI_PIPELINE_SOURCE": source,
        }
        if replace:
            environment["USAGEPEEK_REPLACE_RELEASE_SHA256"] = (
                self.server.old_digest if guard is None else guard)
        with patch.object(publisher, "ROOT", self.root), \
                patch.object(publisher, "request", self.server.request), \
                patch.dict(os.environ, environment, clear=True), \
                patch("sys.argv", [str(SCRIPT)]), redirect_stdout(io.StringIO()):
            publisher.main()

    def assert_no_mutations(self):
        self.assertFalse([event for event in self.server.events if event[0] != "GET"])

    def test_normal_publish_keeps_existing_commit_and_assets_immutable(self):
        with self.assertRaises(RuntimeError):
            self.run_publisher()
        self.assert_no_mutations()
        self.server.events.clear()
        self.server.release["commit"]["id"] = COMMIT
        with self.assertRaises(RuntimeError):
            self.run_publisher()
        self.assert_no_mutations()

    def test_wrong_or_malformed_old_hash_cannot_modify_release(self):
        for guard in ("C" * 64, "invalid-sha"):
            with self.subTest(guard=guard):
                self.server.events.clear()
                with self.assertRaises(RuntimeError):
                    self.run_publisher(replace=True, guard=guard)
                self.assert_no_mutations()

    def test_push_pipeline_cannot_request_replacement(self):
        with self.assertRaises(RuntimeError):
            self.run_publisher(replace=True, source="push")
        self.assert_no_mutations()

    def test_replacement_verifies_new_public_files_before_deleting_old_release(self):
        self.run_publisher(replace=True)
        delete_index = next(index for index, event in enumerate(self.server.events)
                            if event[:2] == ("DELETE", RELEASE_URL))
        for name in ("UsagePeek.exe", "update.json", "SHA256SUMS.txt"):
            public_read = ("GET", NEW_PACKAGE_ROOT + name, False)
            self.assertIn(public_read, self.server.events[:delete_index])
        self.assertEqual(len(self.server.posts), 1)
        links = self.server.release["assets"]["links"]
        self.assertEqual({link["url"] for link in links},
                         {NEW_PACKAGE_ROOT + name for name in
                          ("UsagePeek.exe", "update.json", "SHA256SUMS.txt")})
        self.assertEqual(self.server.release["tag_name"], TAG)
        self.assertEqual(self.server.release["commit"]["id"], COMMIT)

    def test_bad_public_asset_leaves_original_release_intact(self):
        self.server.bad_public_file = NEW_PACKAGE_ROOT + "UsagePeek.exe"
        with self.assertRaises(RuntimeError):
            self.run_publisher(replace=True)
        self.assertNotIn(("DELETE", RELEASE_URL, True), self.server.events)
        self.assertEqual(self.server.release, self.server.original_release)

    def test_release_changed_after_upload_is_not_deleted(self):
        self.server.change_on_second_read = True
        with self.assertRaises(RuntimeError):
            self.run_publisher(replace=True)
        self.assertNotIn(("DELETE", RELEASE_URL, True), self.server.events)

    def test_failed_creation_restores_original_description_and_download_links(self):
        self.server.fail_new_post = True
        with self.assertRaises(RuntimeError):
            self.run_publisher(replace=True)
        self.assertEqual(len(self.server.posts), 2)
        self.assertEqual(self.server.release["name"], self.server.original_release["name"])
        self.assertEqual(self.server.release["description"],
                         self.server.original_release["description"])
        self.assertEqual({item["url"] for item in self.server.release["assets"]["links"]},
                         {item["url"] for item in self.server.original_release["assets"]["links"]})

    def test_retry_of_already_replaced_release_does_not_delete_it(self):
        self.server.install_current_release()
        self.run_publisher(replace=True)
        self.assertFalse([event for event in self.server.events if event[0] == "DELETE"])
        self.assertEqual(self.server.release["commit"]["id"], COMMIT)
        self.assertEqual(self.server.packages[NEW_PACKAGE_ROOT + "update.json"],
                         encode(self.manifest))


if __name__ == "__main__":
    unittest.main(verbosity=2)
