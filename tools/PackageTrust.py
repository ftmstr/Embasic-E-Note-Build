"""Verify exact package/bundle evidence without granting legal or publisher approval."""
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import tarfile
import urllib.request
from urllib.parse import urlparse
from zipfile import ZipFile


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def normalize_module(text, package=False):
    if package:
        text = re.sub(r'\brequire\(\s*[\'"]([^\'"]+)[\'"]\s*\)\.(EventEmitter|Stream)', r'(__REQUIRE__.\2)', text)
        text = re.sub(r'\brequire\(\s*[\'"][^\'"]+[\'"]\s*\)', '__REQUIRE__', text)
    else:
        text = re.sub(r'__nccwpck_require__\(\d+\)', '__REQUIRE__', text)
    return text.strip()


def verify(source, job, retain):
    root = source / "third-party" / "PackageTrust"
    index_path = root / "manifest.json"
    index = json.loads(index_path.read_text())
    archive = root / "package-evidence.zip"
    if digest(archive.read_bytes()) != index["archive_sha256"]:
        raise ValueError("Package evidence archive changed.")
    with ZipFile(archive) as z:
        if len(z.namelist()) != len(index["files"]) or set(z.namelist()) != {f["path"] for f in index["files"]}:
            raise ValueError("Package evidence entries differ.")
        if sum(f.file_size for f in z.infolist()) > 16 * 1024 * 1024:
            raise ValueError("Package evidence exceeds the size bound.")
        for f in index["files"]:
            b = z.read(f["path"])
            if len(b) != f["size_bytes"] or digest(b) != f["sha256"]:
                raise ValueError("Package evidence exact bytes changed.")
        review = json.loads(z.read("package-review.json"))
        old = json.loads((source / "third-party" / "BuildProvenance" / "provenance.json").read_text())
        action = next(a for a in old["actions"] if a["repository"] == review["action_repository"])
        if action["commit"] != review["action_commit"]:
            raise ValueError("Package review belongs to a different Action.")
        with ZipFile(source / "third-party" / "BuildProvenance" / action["source_archive"]["path"]) as old_zip:
            lock = json.loads(old_zip.read("package-lock.json"))
        runner = os.environ.get("GITHUB_ACTIONS") == "true"
        action_root = Path(os.environ["RUNNER_WORKSPACE"]).parent / "_actions" if runner else None
        bundle = None
        if runner:
            bundle = (action_root / action["repository"] / action["commit"] / review["entrypoint"]).read_bytes()
            if digest(bundle) != review["entrypoint_sha256"]:
                raise ValueError("Runner upload bundle differs from the recorded exact Action.")
        results = []
        for p in review["packages"]:
            metadata = json.loads(z.read(p["name"] + "/registry.json"))
            url = p["tarball_url"]
            if urlparse(url).scheme != "https" or urlparse(url).hostname != "registry.npmjs.org":
                raise ValueError("Unexpected package archive origin.")
            with urllib.request.urlopen(url, timeout=30) as response:
                raw = response.read(8 * 1024 * 1024 + 1)
            if len(raw) > 8 * 1024 * 1024:
                raise ValueError("Package archive exceeds the size bound.")
            integrity = "sha512-" + base64.b64encode(hashlib.sha512(raw).digest()).decode()
            if integrity != p["integrity"] or integrity != metadata["dist"]["integrity"]:
                raise ValueError("Package archive and registry integrity differ.")
            if p["lock_path"]:
                if lock["packages"].get(p["lock_path"]) != p["lock_record"] or p["lock_record"]["integrity"] != integrity:
                    raise ValueError("Package archive does not match the retained Action lock.")
            elif any(k.endswith("/" + p["name"]) for k in lock["packages"]):
                raise ValueError("Recorded lock absence is no longer true.")
            with tarfile.open(fileobj=io.BytesIO(raw), mode="r:gz") as tar:
                manifest = json.load(tar.extractfile("package/package.json"))
                main = tar.extractfile(p["main_path"]).read()
                if manifest["name"] != p["name"] or manifest["version"] != p["version"] or digest(main) != p["main_sha256"]:
                    raise ValueError("Package identity or entrypoint differs.")
                for f in p["legal_files"]:
                    if digest(tar.extractfile(f["archive_path"]).read()) != f["sha256"]:
                        raise ValueError("Package legal/readme bytes differ.")
            for match in p["module_matches"]:
                if bundle is not None:
                    module = bundle[match["byte_start"]:match["byte_end"]]
                    if digest(module) != match["module_sha256"]:
                        raise ValueError("Runner bundle module bytes differ.")
                    normalized = normalize_module(module.decode("utf-8"))
                    if normalize_module(main.decode("utf-8"), True) != normalized or digest(normalized.encode()) != match["normalized_body_sha256"]:
                        raise ValueError("Full package main body does not match the documented bundle transformation.")
            results.append({"name": p["name"], "version": p["version"], "integrity": integrity,
                            "lock_status": p["lock_status"], "bundle_status": p["bundle_status"],
                            "runner_module_bytes": ("MATCH" if runner else "NOT_RUN") if p["module_matches"] else "INCLUSION_NOT_PROVEN",
                            "module_ids": [m["module_id"] for m in p["module_matches"]],
                            "legal_acceptance": p["legal_acceptance"]})
        installer_results = []
        setup = next(a for a in old["actions"] if a["repository"] == "actions/setup-dotnet")
        for name in ("install-dotnet.ps1", "install-dotnet.sh"):
            key = "setup-dotnet/externals/" + name
            raw = z.read(key)
            if runner and (action_root / setup["repository"] / setup["commit"] / "externals" / name).read_bytes() != raw:
                raise ValueError("Runner external installer script differs from the exact pinned source.")
            installer_results.append({"path": key, "sha256": digest(raw), "runner_bytes": "MATCH" if runner else "NOT_RUN"})
    return {"retained_files": [retain(index_path, source, "package-evidence", "package evidence index"),
                               retain(archive, source, "package-evidence", "exact registry metadata package module hashes and installer source evidence")],
            "packages": results, "installer_scripts": installer_results,
            "scope": "CI dependencies only; source/package integrity is not legal acceptance or independent publisher trust",
            "release_status": "BLOCKED"}
