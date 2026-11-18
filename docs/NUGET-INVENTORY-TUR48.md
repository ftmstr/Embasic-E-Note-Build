# TUR 48 · Application dependency inventory

W014 / SEC-EMD-09 has a concrete restore-based evidence path. After solution
restore, Windows CI runs `Collect-NugetInventory.py` on the application's
`project.assets.json` and retains JSON, a readable table and package legal
metadata as a workflow artifact. This includes the transitive Blake2 package
previously missing from the top-level NOTICE list.

The collector checks the project's identity, package id/version, cached
archive SHA-512, restore content hash, and selected compile/runtime/build
asset bytes against the original package archive. It records SHA-256 and
preserves nuspec and any packaged LICENSE/NOTICE files. Missing archives,
mismatched cache bytes or incomplete metadata stop collection. A new output
directory and clean tracked source tree prevent silently mixing prior evidence.

For signed NuGet packages, the restore content hash excludes signature metadata.
It differs from the archive hash. Both are retained with the cache source.
This collector does not verify publisher trust or package signatures.

The three currently restored package versions declare MIT. Argon2 and Blake2
have no standalone license file in their archives. Their exact upstream source
license text and redistribution obligations remain REVIEW. The shared runtime,
SDK/build/test tools, fonts, logos and final installer are separate open items.
V1.0 remains BLOCKED and the WPF secure note remains DISABLED.

References

- https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution
- https://learn.microsoft.com/en-us/nuget/reference/nuspec
- https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File
