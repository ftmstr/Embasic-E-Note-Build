# TUR 49 · Component and redistribution review

W014 / SEC-EMD-09 remains IN PROGRESS. The application dependency artifact now
includes the exact Konscious license text, source project metadata and source
provenance. It also records the build SDK, Python version and installed shared
runtime names and versions. Installed runtimes are an environment inventory;
the final distribution contents require their own review.

| Component | Current evidence | Open acceptance |
|---|---|---|
| Argon2 1.3.1 / Blake2 1.1.1 | Nuspec source commit, original 2017 MIT license, 2024 package/project copyright, exact hashes | Package signatures and binary/source reproducibility, final distribution notice placement |
| .NET / Windows Desktop shared runtime | Framework references and SDK pruning ranges; installed runtime versions captured per CI worker | Exact distribution mode, redistributed runtime files and their LICENSE/NOTICE |
| .NET SDK | `dotnet --version` in inventory and SDK information in workflow log | SDK/tool dependency and legal-file inventory if redistributed |
| GitHub Actions | checkout, setup-dotnet, setup-java, upload-artifact at v4 in Windows workflow | Full action version/provenance/license inventory |
| Python / pypdf | Worker Python version; test-only pypdf 6.21.3 wheel pinned to SHA-256 `c8ca4d9a360dbd5d4b9bb7a8389e2a9ae6a106bee722b496f918040ba2daa17e` | Exact Python and wheel legal files and final tool SBOM |
| Temurin Java 8 | Test-only setup-java configuration | Exact JDK version and its legal-file inventory |
| Official UYAP 5.4.20 | Test-only official MSI, SHA-256 `ed567324c80c45847fb28f4bab50e4db65f2487e51f221d0ecf29a46744b5694` | Keep vendor binaries outside app distribution; vendor redistribution rights REVIEW |
| Segoe UI / Segoe UI Emoji / Consolas | System font names referenced by the WPF source; no font files tracked | Exact host-font provenance and PDF embedding/distribution rights |
| Liberation Serif / Liberation Mono | Names used in synthetic UDF test specimens | Host font availability and rendering acceptance; no font binaries bundled |
| Embasic logos and icons | Four tracked assets and their unchanged SHA-256 values below | User ownership and redistribution clearance |
| Installer | No installer built for this round | Complete installed-file inventory and all P0 acceptance gates |

The upstream commit is
`4ed95a5377e411506ca6868409b5c7d7ecaa859b`. Its root MIT license says
`Copyright (c) 2017 Keef Aragon`. The same commit's Argon2/Blake2 projects
declare the expected package versions and `(c) 2024 Keef Aragon`.
The original license bytes are preserved, and both attributions are retained.
The 22 C# files in the two package directories were scanned for license,
copyright and source-reference lines. The matches reference the PHC Argon2
algorithm repository and are recorded in `source-header-review.json`.
Binary/source reproducibility and complete legal clearance remain open.

| Asset | SHA-256 |
|---|---|
| Logo-Light.png | `8bc5042bdd8b8303c59fba0a307e63d0d3669e9f9d0b129183119d5372e25948` |
| Logo-Dark.png | `17f224da2fccce6164e605b8bc081c43c6d62b9b523ca3293964b400f5f1691a` |
| Embasic-E-Note.ico | `9ac84f3a2a81c65ef33a86fcc40cd0b36851fd480ba5b30275090589f6d49dca` |
| Embasic-E-Note-Icon-512.png | `c4e45134d86358a07e593dc31860a3c3e6a3423caa1aecc1c6bc24f05004bdbf` |

Physical Windows, printer, four-edge measurement, DPI and physical security
remain NOT RUN. WPF secure .emd remains DISABLED and V1.0 BLOCKED.
