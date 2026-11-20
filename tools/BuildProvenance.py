"""Verify retained upstream bytes and link observed build declarations to source snapshots."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
from zipfile import ZipFile


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def blob(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()


def verify(source, job, retain):
    root = source / "third-party" / "BuildProvenance"
    metadata = root / "provenance.json"
    index = json.loads(metadata.read_text(encoding="utf-8"))
    if index["schema_version"] != 1 or index["release_status"] != "BLOCKED":
        raise ValueError("Unexpected provenance schema or release status.")
    retained = [retain(metadata, source, "source-evidence", "upstream provenance index")]
    for item in index["actions"] + index["dotnet_vmr"]:
        archive = item["source_archive"]
        relative = PurePosixPath(archive["path"])
        if relative.is_absolute() or len(relative.parts) != 1 or "\\" in archive["path"]:
            raise ValueError("Invalid provenance archive path.")
        path = root / relative.name
        if sha(path.read_bytes()) != archive["sha256"]:
            raise ValueError("Upstream evidence archive hash changed.")
        with ZipFile(path) as z:
            expected = {f["path"]: f for f in archive["files"]}
            if len(z.namelist()) != len(expected) or set(z.namelist()) != set(expected):
                raise ValueError("Duplicate, missing or extra upstream evidence entries.")
            if sum(f.file_size for f in z.infolist()) > 16 * 1024 * 1024:
                raise ValueError("Upstream evidence exceeds the size bound.")
            for name, f in expected.items():
                raw = z.read(name)
                if len(raw) != f["size_bytes"] or sha(raw) != f["sha256"] or blob(raw) != f["git_blob"]:
                    raise ValueError("Upstream exact byte/Git blob verification failed.")
            if item in index["dotnet_vmr"]:
                manifest = json.loads(z.read("src/source-manifest.json"))
                for component in item["components"]:
                    if component not in manifest["repositories"]:
                        raise ValueError("VMR component mapping differs from the original source manifest.")
        retained.append(retain(path, source, "source-evidence", "exact upstream legal/source declarations archive"))

    workflow_bytes = (source / ".github" / "workflows" / "windows-ci.yml").read_bytes()
    workflow = workflow_bytes.decode("utf-8")
    pins = {a["repository"]: a["commit"] for a in index["actions"]}
    uses = re.findall(r"^\s+-\s+uses:\s+(\S+)", workflow, re.M)
    if not uses or any(u not in {f"{r}@{c}" for r, c in pins.items()} for u in uses):
        raise ValueError("Workflow action refs differ from the exact recorded source commits.")
    if {u.split("@")[0] for u in uses} != set(pins):
        raise ValueError("Workflow and recorded action set differ.")
    block = re.search(r"^  " + re.escape(job) + r":\s*\n(.*?)(?=^  [\w-]+:|\Z)", workflow, re.M | re.S)
    requested = set(re.findall(r"^\s+-\s+uses:\s+(\S+)", block[1], re.M)) if block else set()
    runner = os.environ.get("GITHUB_ACTIONS") == "true"
    if runner and not block:
        raise ValueError("Unknown workflow job prevents action provenance verification.")
    action_results = []
    for action in index["actions"]:
        ref = action["repository"] + "@" + action["commit"]
        result = {"repository": action["repository"], "commit": action["commit"],
                  "requested_by_job": ref in requested,
                  "legal_declarations": action["legal_declaration_count"],
                  "runner_bytes": "NOT_RUN" if not runner else "NOT_REQUESTED"}
        if runner and ref in requested:
            action_root = Path(os.environ["RUNNER_WORKSPACE"]).parent / "_actions" / action["repository"] / action["commit"]
            count = 0
            for f in action["source_archive"]["files"] + action["runtime_entrypoints"]:
                path = action_root.joinpath(*PurePosixPath(f["path"]).parts)
                raw = path.read_bytes()
                if len(raw) != f["size_bytes"] or sha(raw) != f["sha256"] or blob(raw) != f["git_blob"]:
                    raise ValueError(f"Runner action bytes differ from retained source: {ref}/{f['path']}")
                count += 1
            result.update(runner_bytes="MATCH", checked_files=count,
                          runtime_entrypoints=action["runtime_entrypoints"])
        action_results.append(result)
    return index, {"retained_files": retained, "workflow_sha256": sha(workflow_bytes),
                   "actions": action_results, "release_status": "BLOCKED"}


def link_component(component, index):
    declaration = component.get("source_revision_declaration")
    if not declaration:
        return
    revision = declaration.splitlines()[0].strip()
    snapshot = next((x for x in index["dotnet_vmr"] if x["commit"] == revision), None)
    if snapshot is None:
        component["verified_source_link"] = {"status": "UNMAPPED", "declared_revision": revision}
        return
    category = "sdk" if component["name"] == ".NET SDK" else "runtime"
    if component["version"] not in snapshot["observed_versions"][category]:
        raise ValueError("Version and source revision conflict with the recorded observed mapping.")
    names = {".NET SDK": ["sdk"], "Microsoft.NETCore.App": ["runtime"],
             "Microsoft.AspNetCore.App": ["aspnetcore"],
             "Microsoft.WindowsDesktop.App": ["windowsdesktop", "wpf", "runtime"]}[component["name"]]
    repos = [x for x in snapshot["components"] if x["path"] in names]
    source_legal = [f for f in snapshot["source_archive"]["files"]
                    if any(f["path"].startswith("src/" + name + "/") for name in names)]
    installed_legal = [f for f in component["legal_files"] + component.get("installation_legal_files", [])
                       if f["role"] == "installed legal text"]
    # A byte match is evidence of that text only; different source/binary terms stay distinct.
    comparisons = [{"source_path": f["path"], "source_sha256": f["sha256"],
                    "same_installed_text_paths": [g["installed_path"] for g in installed_legal if g["sha256"] == f["sha256"]]}
                   for f in source_legal]
    component["source_mapping"] = "DECLARATION_LINKED_TO_VMR_SNAPSHOT; binary/source reproducibility REVIEW"
    component["verified_source_link"] = {"status": "DECLARATION_LINKED_TO_SNAPSHOT",
                                         "repository": snapshot["repository"], "commit": revision,
                                         "component_repositories": repos,
                                         "source_archive": snapshot["source_archive"]["path"],
                                         "legal_text_comparisons": comparisons,
                                         "binary_distribution_legal_applicability": "REVIEW"}
