"""Record restored application packages and their archive-bound license evidence."""
import argparse
import base64
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
from xml.etree import ElementTree
from zipfile import ZipFile


ASSET_GROUPS = ("compile", "runtime", "native", "resource", "runtimeTargets", "build", "buildMultiTargeting",
                "buildTransitive", "contentFiles")


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def inventory(assets_path, output, source_root):
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    project = source_root / "src" / "Embasic.ENote.App" / "Embasic.ENote.App.csproj"
    if Path(assets["project"]["restore"]["projectPath"]).resolve() != project.resolve():
        raise ValueError("The restore graph does not belong to this application's source tree.")
    targets = assets["targets"]
    libraries = assets["libraries"]
    folders = [Path(folder) for folder in assets["packageFolders"]]
    selected = sorted({name for target in targets.values() for name, data in target.items()
                       if data["type"] == "package"})
    if not selected:
        raise ValueError("The application restore graph contains no packages.")
    packages = []
    evidence = {}
    for name in selected:
        package_id, version = name.rsplit("/", 1)
        library = libraries[name]
        relative = PurePosixPath(library["path"])
        if relative.is_absolute() or ".." in relative.parts or "\\" in library["path"]:
            raise ValueError(f"Invalid package cache path for {name}.")
        candidates = [folder.joinpath(*relative.parts) for folder in folders]
        package_dir = next((path for path in candidates if path.is_dir()), None)
        if package_dir is None:
            raise FileNotFoundError(f"Package cache entry is missing for {name}.")
        archive_path = package_dir / f"{package_id.lower()}.{version.lower()}.nupkg"
        archive_data = archive_path.read_bytes()
        archive_hash = base64.b64encode(hashlib.sha512(archive_data).digest()).decode("ascii")
        cached_archive_hash = Path(str(archive_path) + ".sha512").read_text(encoding="utf-8-sig").strip()
        restore_metadata = json.loads((package_dir / ".nupkg.metadata").read_text(encoding="utf-8-sig"))
        if archive_hash != cached_archive_hash or restore_metadata["contentHash"] != library["sha512"]:
            raise ValueError(f"Archive or restore content hash differs from the NuGet cache records for {name}.")
        selected_assets = sorted({path for target in targets.values() for group in ASSET_GROUPS
                                  for path in target.get(name, {}).get(group, {})})
        with ZipFile(archive_path) as archive:
            specs = [path for path in archive.namelist() if "/" not in path and path.lower().endswith(".nuspec")]
            if len(specs) != 1:
                raise ValueError(f"Expected one package nuspec in {name}.")
            spec_data = archive.read(specs[0])
            metadata = ElementTree.fromstring(spec_data).find("{*}metadata")
            if metadata is None:
                raise ValueError(f"Missing nuspec metadata in {name}.")
            if metadata.findtext("{*}id", "").lower() != package_id.lower() or metadata.findtext("{*}version") != version:
                raise ValueError(f"The nuspec identity differs from the restore graph for {name}.")
            license_node = metadata.find("{*}license")
            license_type = license_node.get("type") if license_node is not None else None
            license_value = license_node.text if license_node is not None else None
            legal_paths = {path for path in archive.namelist() if not path.endswith("/") and
                           re.match(r"^(license|licence|notice|copying|third-party-notices)(\.|$)", PurePosixPath(path).name, re.I)}
            if license_type == "file":
                legal_paths.add(license_value)
            legal_paths.add(specs[0])
            legal_files = []
            for path in sorted(legal_paths):
                raw = archive.read(path)
                if len(raw) > 1024 * 1024:
                    raise ValueError(f"Legal evidence exceeds the size bound in {name}.")
                location = f"{package_id.lower()}/{version}/{path}"
                evidence[location] = raw
                legal_files.append({"archive_path": path, "sha256": sha256(raw), "evidence_path": location})
            file_hashes = []
            for path in selected_assets:
                parts = PurePosixPath(path)
                if parts.is_absolute() or ".." in parts.parts or "\\" in path:
                    raise ValueError(f"Invalid restored asset path in {name}.")
                archived = archive.read(path)
                expanded = package_dir.joinpath(*parts.parts).read_bytes()
                if expanded != archived:
                    raise ValueError(f"Expanded cache asset differs from archive: {name}/{path}.")
                file_hashes.append({"path": path, "sha256": sha256(archived)})
        packages.append({"id": package_id, "version": version,
                         "purl": f"pkg:nuget/{package_id}@{version}",
                         "archive_sha512_base64": archive_hash, "archive_sha256": sha256(archive_data),
                         "restore_content_hash": library["sha512"],
                         "cache_source": restore_metadata.get("source"),
                         "nuspec_sha256": sha256(spec_data),
                         "authors": metadata.findtext("{*}authors"),
                         "copyright": metadata.findtext("{*}copyright"),
                         "repository": metadata.find("{*}repository").attrib if metadata.find("{*}repository") is not None else {},
                         "license": {"type": license_type, "value": license_value,
                                     "legacy_url": metadata.findtext("{*}licenseUrl"), "review": "REVIEW"},
                         "legal_files": legal_files, "selected_assets": file_hashes})
    head = subprocess.check_output(["git", "-C", str(source_root), "rev-parse", "HEAD"], text=True).strip()
    if subprocess.check_output(["git", "-C", str(source_root), "status", "--porcelain", "--untracked-files=no"], text=True):
        raise ValueError("Tracked source changes prevent a commit-bound inventory.")
    graph = {target: {name: {"type": item["type"], "dependencies": item.get("dependencies", {})}
                      for name, item in sorted(items.items())} for target, items in sorted(targets.items())}
    project_paths = [project] + [(project.parent / item["msbuildProject"]).resolve()
                                 for item in libraries.values() if item["type"] == "project"]
    declarations = []
    for path in sorted(project_paths):
        relative_path = path.relative_to(source_root).as_posix()
        for node in ElementTree.parse(path).findall(".//PackageReference"):
            declarations.append({"project": relative_path, "id": node.get("Include"),
                                 "version": node.get("Version"), "condition": node.get("Condition")})
    frameworks = {name: {"framework_references": value.get("frameworkReferences", {}),
                         "pruning_ranges_for_declared_packages": {
                             item["id"]: value["packagesToPrune"][item["id"]] for item in declarations
                             if item["id"] in value.get("packagesToPrune", {})}}
                  for name, value in assets["project"]["frameworks"].items()}
    result = {"schema_version": 1, "source_commit": head, "application": "Embasic.ENote.App",
              "assets_sha256": sha256(assets_path.read_bytes()), "scope": "restored application NuGet graph",
              "release_status": "BLOCKED", "package_count": len(packages), "packages": packages,
              "targets": graph, "declared_package_references": declarations, "frameworks": frameworks,
              "open_reviews": ["Package license text and distribution obligations", "Shared .NET and Windows runtime",
                               "Build and test tools", "Fonts and logo redistribution rights", "Final installer inventory"]}
    output.mkdir(parents=True, exist_ok=False)
    for path, raw in evidence.items():
        parts = PurePosixPath(path)
        if parts.is_absolute() or ".." in parts.parts or "\\" in path:
            raise ValueError("Invalid legal evidence output path.")
        destination = output / "legal" / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(raw)
    (output / "nuget-inventory.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    rows = []
    for package in packages:
        license_info = package["license"]
        description = f'{license_info["type"]}: {license_info["value"]}' if license_info["type"] else "Legacy URL only · REVIEW"
        rows.append(f'| {package["id"]} | {package["version"]} | {description} | {len(package["legal_files"])} |')
    summary = (f"# Restored application NuGet inventory\n\nSource commit `{head}`. Release BLOCKED.\n\n"
               "| Package | Version | Declared license | Legal evidence files |\n|---|---|---|---|\n" +
               "\n".join(rows) + "\n\nArchive SHA-512 matches the cached archive hash. Restore content hash matches NuGet metadata. "
               "Selected expanded assets match archive bytes. Package signatures require separate verification.\n"
               "License declarations and archived texts require distribution review. Shared runtime, build/test tools, "
               "fonts, logos and installer contents remain open.\n")
    (output / "nuget-inventory.md").write_text(summary, encoding="utf-8")
    print(f"Collected {len(packages)} application NuGet packages and {len(evidence)} archive-bound legal evidence files.")
    print(f"Source commit {head}; release BLOCKED.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    inventory(args.assets.resolve(), args.output.resolve(), args.source_root.resolve())
