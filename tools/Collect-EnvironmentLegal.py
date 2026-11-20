"""Retain installed CI tool legal bytes and their observed provenance, not release approval."""
import argparse
import base64
import csv
import hashlib
import importlib.metadata
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from BuildProvenance import verify as verify_build_provenance, link_component


LEGAL = re.compile(r"^(licen[cs]e|notice|copying|copyright|third.?party(?:notices)?|assembly_exception)([._-]|$)", re.I)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def command(*args):
    return subprocess.check_output(args, text=True, encoding="utf-8", errors="replace").strip()


def collect(output, source, job, include_java=False, include_pypdf=False):
    if output.exists():
        raise ValueError("Use a new output directory to avoid retaining stale evidence.")
    if command("git", "-C", str(source), "status", "--porcelain", "--untracked-files=no"):
        raise ValueError("Tracked source changes prevent commit-bound evidence.")
    evidence = {}
    components = []

    def retain(path, root, label, role):
        path, root = Path(path), Path(root)
        if not path.is_file():
            raise FileNotFoundError(path)
        raw = path.read_bytes()
        if len(raw) > 8 * 1024 * 1024:
            raise ValueError(f"Evidence size bound exceeded: {path.name}")
        relative = path.relative_to(root).as_posix()
        key = f"{label}/{relative}"
        if key in evidence and evidence[key] != raw:
            raise ValueError("Conflicting evidence paths.")
        evidence[key] = raw
        return {"installed_path": str(path), "evidence_path": key, "role": role,
                "size_bytes": len(raw), "sha256": digest(raw)}

    def legal(root, label, recursive=False):
        files = root.rglob("*") if recursive else root.iterdir()
        return [retain(p, root, label, "installed legal text") for p in sorted(files)
                if p.is_file() and (LEGAL.match(p.name) or "legal" in p.relative_to(root).parts[:-1])]

    def anchor(path):
        path = Path(path)
        return {"installed_path": str(path), "size_bytes": path.stat().st_size,
                "sha256": digest(path.read_bytes())}

    # --list-sdks reports the selected host installation root without relying on DOTNET_ROOT.
    sdk_version = command("dotnet", "--version")
    sdk_rows = command("dotnet", "--list-sdks").splitlines()
    sdk_match = next((re.fullmatch(re.escape(sdk_version) + r" \[(.+)\]", row) for row in sdk_rows
                      if row.startswith(sdk_version + " [")), None)
    if not sdk_match:
        raise ValueError("Selected SDK is absent from the host inventory.")
    dotnet_root = Path(sdk_match[1]).parent
    root_legal = legal(dotnet_root, "dotnet-installation")
    if not any("license" in p["evidence_path"].lower() for p in root_legal) or not any(
            "thirdparty" in p["evidence_path"].lower() for p in root_legal):
        raise ValueError("The .NET installation LICENSE/ThirdPartyNotices are missing.")
    host_path = Path(shutil.which("dotnet")).resolve()
    sdk_path = dotnet_root / "sdk" / sdk_version
    sdk_files = legal(sdk_path, f"dotnet-sdk/{sdk_version}")
    sdk_files.append(retain(sdk_path / ".version", sdk_path, f"dotnet-sdk/{sdk_version}", "installed source/version declaration"))
    components.append({"name": ".NET SDK", "version": sdk_version,
                       "scope": "build tool", "legal_files": sdk_files,
                       "installation_legal_files": root_legal,
                       "binary_anchors": [anchor(host_path), anchor(sdk_path / "dotnet.dll")],
                       "source_repository_candidates": ["https://github.com/dotnet/sdk", "https://github.com/dotnet/dotnet"],
                       "source_revision_declaration": (sdk_path / ".version").read_text(encoding="utf-8-sig"),
                       "source_mapping": "REVIEW: installed declaration; upstream repository mapping not verified"})
    runtime_rows = command("dotnet", "--list-runtimes").splitlines()
    for row in runtime_rows:
        match = re.fullmatch(r"(\S+) (\S+) \[(.+)\]", row)
        if not match:
            raise ValueError("Unrecognized runtime inventory row.")
        name, version, parent = match.groups()
        root = Path(parent) / version
        label = f"dotnet-runtime/{name}/{version}"
        files = legal(root, label)
        version_file = root / ".version"
        if version_file.exists():
            files.append(retain(version_file, root, label, "installed source/version declaration"))
        binary = root / ("coreclr.dll" if os.name == "nt" else "libcoreclr.so")
        if name == "Microsoft.WindowsDesktop.App":
            binary = root / "PresentationFramework.dll"
        elif name == "Microsoft.AspNetCore.App":
            binary = root / "Microsoft.AspNetCore.dll"
        deps = root / f"{name}.deps.json"
        if deps.exists():
            files.append(retain(deps, root, label, "installed dependency metadata"))
        components.append({"name": name, "version": version, "scope": "installed shared runtime; use not inferred",
                           "legal_files": files, "installation_legal_files": root_legal,
                           "binary_anchors": [anchor(binary)],
                           "source_revision_declaration": version_file.read_text(encoding="utf-8-sig") if version_file.exists() else None,
                           "source_mapping": "REVIEW: per-runtime legal applicability and upstream mapping not verified"})

    python_root = Path(sys.base_prefix)
    python_files = legal(python_root, "python")
    if os.name == "nt" and not any("license" in p["evidence_path"].lower() for p in python_files):
        raise ValueError("The installed Windows Python LICENSE is missing.")
    components.append({"name": "CPython", "version": sys.version.split()[0], "scope": "test/inventory tool",
                       "legal_files": python_files, "binary_anchors": [anchor(sys.executable)],
                       "base_prefix": str(python_root), "version_banner": sys.version,
                       "source_repository": "https://github.com/python/cpython",
                       "source_tag_candidate": "v" + sys.version.split()[0],
                       "source_build_declaration": list(getattr(sys, "_git", ())),
                       "source_mapping": "REVIEW: version-derived tag; no binary/source reproducibility claim"})

    if include_pypdf:
        dist = importlib.metadata.distribution("pypdf")
        paths = list(dist.files or [])
        metadata_path = next(p for p in paths if str(p).endswith(".dist-info/METADATA"))
        dist_root = Path(dist.locate_file(metadata_path)).parent
        files = legal(dist_root, "pypdf", recursive=True)
        if not files:
            raise ValueError("The installed pypdf license is missing.")
        for name in ("METADATA", "RECORD", "WHEEL", "INSTALLER"):
            path = dist_root / name
            if path.exists():
                files.append(retain(path, dist_root, "pypdf", "installed package metadata"))
        # RECORD is an installed-wheel consistency check, not an authenticated trust root.
        records = {row[0]: row for row in csv.reader(io.StringIO((dist_root / "RECORD").read_text(encoding="utf-8")))}
        for entry in files:
            path = Path(entry["installed_path"])
            key = next((str(p).replace("\\", "/") for p in paths if Path(dist.locate_file(p)).resolve() == path.resolve()), None)
            row = records.get(key)
            if path.name != "RECORD":
                if not row or not row[1].startswith("sha256="):
                    raise ValueError("pypdf evidence is missing its installed RECORD hash.")
                expected = base64.urlsafe_b64encode(bytes.fromhex(entry["sha256"])).decode().rstrip("=")
                if row[1] != "sha256=" + expected or int(row[2]) != entry["size_bytes"]:
                    raise ValueError("pypdf installed legal/metadata bytes differ from RECORD.")
        requirement = source / "tools" / "tur44-pdf-requirements.txt"
        if not re.search(r"pypdf==" + re.escape(dist.version) + r"(?:\s|$)", requirement.read_text()):
            raise ValueError("Installed pypdf does not match the repository requirement.")
        files.append(retain(requirement, source, "pypdf", "repository wheel version/hash pin"))
        components.append({"name": "pypdf", "version": dist.version, "scope": "PDF test verifier only",
                           "legal_files": files, "installed_record_consistency": "MATCH",
                           "project_urls": dist.metadata.get_all("Project-URL"),
                           "source_repository": "https://github.com/py-pdf/pypdf",
                           "source_tag_candidate": dist.version,
                           "source_mapping": "REVIEW: package metadata and version pin; wheel/source comparison open"})

    if include_java:
        java_root = Path(os.environ["JAVA_HOME"])
        java_files = legal(java_root, "java", recursive=False)
        legal_root = java_root / "legal"
        if legal_root.is_dir():
            java_files.extend(legal(legal_root, "java/legal", recursive=True))
        if not any("license" in p["evidence_path"].lower() for p in java_files):
            raise ValueError("Installed Java LICENSE is missing.")
        java_files.append(retain(java_root / "release", java_root, "java", "vendor build/source declaration"))
        java_bin = java_root / "bin" / ("java.exe" if os.name == "nt" else "java")
        version = subprocess.run([str(java_bin), "-version"], capture_output=True, text=True, check=True)
        components.append({"name": "Temurin OpenJDK", "version_banner": version.stdout + version.stderr,
                           "scope": "official UYAP synthetic acceptance tool only", "legal_files": java_files,
                           "binary_anchors": [anchor(java_bin)],
                           "vendor_release_declaration": (java_root / "release").read_text(encoding="utf-8"),
                           "source_repository_candidates": ["https://github.com/adoptium/temurin8-binaries", "https://github.com/adoptium/jdk8u"],
                           "source_mapping": "REVIEW: vendor release metadata retained; source/build match open"})

    provenance_index, source_evidence = verify_build_provenance(source, job, retain)
    for component in components:
        link_component(component, provenance_index)
    manifest = {"schema_version": 2, "source_commit": command("git", "-C", str(source), "rev-parse", "HEAD"),
                "workflow_job": job, "workflow_run_id": os.environ.get("GITHUB_RUN_ID"),
                "workflow_run_attempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
                "platform": sys.platform, "scope": "observed installed CI environment; not a distribution SBOM",
                "release_status": "BLOCKED", "components": components, "build_provenance": source_evidence,
                "open_reviews": ["Authenticode/package signatures and trust roots", "binary/source reproducibility",
                                 "per-version runtime legal applicability", "GitHub Actions exact source/license inventory",
                                 "font embedding and logo distribution rights", "final installer file and license inventory"]}
    output.mkdir(parents=True)
    for key, raw in evidence.items():
        target = output / key
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    (output / "environment-legal.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Retained {len(evidence)} exact files for {len(components)} installed components; release BLOCKED.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--java", action="store_true")
    parser.add_argument("--pypdf", action="store_true")
    args = parser.parse_args()
    collect(args.output.resolve(), Path(__file__).resolve().parents[1], args.job, args.java, args.pypdf)
