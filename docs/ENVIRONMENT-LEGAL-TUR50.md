# TUR 50: installed runtime and tool legal evidence

W014 / SEC-EMD-09 is IN PROGRESS. This round retains observed CI legal files
without changing product code or adding product tests. The existing Konscious
license bytes and both 2017/2024 attributions remain unchanged.

| Work item | Acceptance | Status before Windows CI |
| --- | --- | --- |
| W014-ENV-01 | Record selected SDK, every listed shared runtime, installation legal texts, `.version` declarations, dependency metadata and binary anchor hashes | REVIEW |
| W014-ENV-02 | Retain Windows CPython LICENSE and installed pypdf license/METADATA/RECORD/WHEEL/INSTALLER; compare legal/metadata hashes with installed RECORD and version with the repository pin | REVIEW |
| W014-ENV-03 | Retain test-only Temurin LICENSE, third-party notices, exceptions, release/source declarations and executable hash | REVIEW |
| W014-ENV-04 | Download resulting Windows artifacts and independently verify manifest hashes and source commit | READY |

`tools/Collect-EnvironmentLegal.py` runs in the existing build-and-smoke,
tur39-wpf-udf and tur45-windows-pdf-save-dialog jobs. The collector copies raw
bytes; it does not normalize text or reconstruct license wording. The three
artifacts include `environment-legal.json`, exact legal texts and installed
version/dependency metadata. Hidden `.version` files are explicitly included
by upload-artifact. No product or vendor binary is uploaded by this collector.

The manifest records the checkout commit, job and run identity. Selected SDK
discovery uses the host's `--list-sdks` result. All installed shared runtimes
are recorded as environment observations; this does not assert which runtime
each test selected. Executable/library hashes are identifiers, not signature
or source reproducibility checks. A new output directory is required, and
tracked source changes are rejected. Missing required .NET installation,
Windows Python, pypdf or Java legal files fail collection.

Installed `.version`, CPython build information, pypdf Project-URL metadata,
the repository's wheel hash pin and Temurin's `release` file are preserved as
source/build declarations. Source repository/tag candidates are labelled as
unverified. In particular, an installed .NET commit is not automatically
attributed to a guessed component repository: source-build and component
commit mappings need separate upstream verification.

Installed-wheel RECORD consistency is not an independent trust root. The
existing pip `--require-hashes --only-binary=:all:` installation is retained.
No wheel/source reproducibility, signer trust or binary integrity approval is
inferred from RECORD. Root .NET legal files describe the observed installation;
their applicability to every installed runtime and a future distribution is
still REVIEW, especially where per-runtime legal files are absent.

The inventory is not a final distribution SBOM. GitHub Actions exact source
and legal inventory, package/Authenticode signatures, upstream source/build
mapping, binary/source reproducibility, font embedding, logo redistribution
and installer contents remain open. Official UYAP remains test-only and is
not bundled. Existing component/asset evidence is in COMPONENT-REVIEW-TUR49.md.

Physical Windows, printer, four-edge measurements, DPI and physical security
are NOT RUN in the accessible Linux workspace. The TUR46 synthetic A4 physical
procedure remains pending. Secure `.emd` stays DISABLED; V1.0 is BLOCKED.
No main merge or installer release is authorized by these artifacts.

Engineering estimate remains the previous 98%; this is not an acceptance
percentage. This round provides legal evidence toward W014, not release GO.
