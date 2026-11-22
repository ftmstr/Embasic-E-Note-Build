[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InventoryDirectory)
$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'This collector requires Windows; no physical acceptance is implied.' }
$directory = (Resolve-Path $InventoryDirectory).Path
$inventory = Get-Content (Join-Path $directory 'environment-legal.json') -Raw | ConvertFrom-Json
$output = Join-Path $directory 'windows-trust/native-scope'
if (Test-Path $output) { throw 'Use a new native scope evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref/*.dll')).FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'NativeImageInventory.cs') -ReferencedAssemblies $refs
$policyRoot = (Resolve-Path (Join-Path $PSScriptRoot '../third-party/NativeScope')).Path
$policy = Get-Content (Join-Path $policyRoot 'manifest.json') -Raw | ConvertFrom-Json
$retainedReferences = @()
foreach ($f in $policy.original_files) {
    $path = Join-Path $policyRoot $f.path
    $raw = [IO.File]::ReadAllBytes($path)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)).ToLowerInvariant()
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($raw.Length)`0")
    $blob = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([byte[]]($header + $raw))).ToLowerInvariant()
    if ($hash -ne $f.sha256 -or $blob -ne $f.git_blob -or $raw.Length -ne $f.size_bytes) { throw 'Original scope reference bytes changed.' }
    $target = Join-Path $output ('references/' + $f.path)
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    [IO.File]::WriteAllBytes($target,$raw)
    $retainedReferences += @{path=('references/' + $f.path);sha256=$hash;git_blob=$blob;repository=$f.repository;commit=$f.commit;source_path=$f.source_path}
}
[IO.File]::Copy((Join-Path $policyRoot 'manifest.json'),(Join-Path $output 'scope-policy.json'))
$script:LegalBytes = 0
function Save-Legal([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -gt 8MB -or $script:LegalBytes + $file.Length -gt 16MB) {
        return @{installed_path=$file.FullName;size_bytes=$file.Length;status='NOT_RETAINED_SIZE_BOUND';scope='Legal coverage incomplete, REVIEW'}
    }
    $raw = [IO.File]::ReadAllBytes($file.FullName)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)).ToLowerInvariant()
    $name = 'legal/' + $hash + '-' + $file.Name
    New-Item -ItemType Directory -Force -Path (Join-Path $output 'legal') | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $output $name),$raw)
    $script:LegalBytes += $raw.Length
    return @{installed_path=$file.FullName;path=$name;sha256=$hash;size_bytes=$raw.Length;scope='Accompanying file observed at this SDK root; exact binary applicability REVIEW'}
}
function Certificate($Cert) {
    if ($null -eq $Cert) { return $null }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Cert.RawData)).ToLowerInvariant()
    New-Item -ItemType Directory -Force -Path (Join-Path $output 'certificates') | Out-Null
    $name = 'certificates/' + $hash + '.cer'
    [IO.File]::WriteAllBytes((Join-Path $output $name),$Cert.RawData)
    return @{subject=$Cert.Subject;issuer=$Cert.Issuer;sha256=$hash;path=$name;not_before=$Cert.NotBefore.ToUniversalTime().ToString('o');not_after=$Cert.NotAfter.ToUniversalTime().ToString('o')}
}
function Binary([string]$Path, $Image, [string]$Component, [string]$Version, [string]$Origin) {
    $file = Get-Item -LiteralPath $Path
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    $category = 'MIT_SCOPE_INFORMATION_ONLY'
    if ($file.Name -match '^(coreclr|PresentationNative_cor3|vcruntime140_cor3|wpfgfx_cor3)\.dll$' -or $file.Name -match '^Microsoft\.DiaSymReader\.Native\..+\.dll$') { $category='DOTNET_LIBRARY_SCOPE_INFORMATION' }
    if ($file.Name -match '^d3dcompiler_47(_cor3)?\.dll$') { $category='WINDOWS_SDK_SCOPE_INFORMATION' }
    return [ordered]@{installed_path=$file.FullName;name=$file.Name;size_bytes=$file.Length
        sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        component=$Component;component_version=$Version;origin=$Origin;pe=$Image
        file_version=$info.FileVersion;product_version=$info.ProductVersion;original_filename=$info.OriginalFilename;company=$info.CompanyName
        os_authenticode_status=$signature.Status.ToString();signature_type=[string]$signature.SignatureType
        signer=(Certificate $signature.SignerCertificate);timestamp_signer=(Certificate $signature.TimeStamperCertificate)
        scope_category=$category;legal_acceptance='REVIEW';sdk_redist_matches=@()
        scope='Installed bytes and OS signature observation only. Informative category is not per-version distribution approval.'}
}
$binaries = @()
$discovery = @()
foreach ($component in $inventory.components | Where-Object name -eq 'Microsoft.WindowsDesktop.App') {
    $root = Split-Path $component.binary_anchors[0].installed_path -Parent
    $dlls = @(Get-ChildItem -LiteralPath $root -File -Filter '*.dll')
    if ($dlls.Count -gt 512) { throw 'Runtime DLL discovery size bound exceeded.' }
    $selected = @(); $managedNative = @()
    foreach ($dll in $dlls) {
        $image = [NativeImageInventory]::Read($dll.FullName)
        if ($image.HasManagedNativeHeader) { $managedNative += $dll.Name }
        if ($image.NativeOrMixed) {
            $binaries += Binary $dll.FullName $image $component.name $component.version 'installed WindowsDesktop runtime root'
            $selected += $dll.Name
        }
    }
    $required = @('PresentationNative_cor3.dll','vcruntime140_cor3.dll','wpfgfx_cor3.dll','D3DCompiler_47_cor3.dll')
    $discovery += @{component=$component.name;version=$component.version;root=$root;root_dlls_examined=$dlls.Count;native_or_mixed=$selected;managed_native_header_files=$managedNative
        required_scope_files=@($required | ForEach-Object { @{name=$_;present=(Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf);recorded=($_ -in $selected)} })
        installation_legal_files=$component.installation_legal_files
        scope='Runtime-root DLL headers only; not all subdirectories/resources, loaded module proof, publish output or installer SBOM'}
}
foreach ($component in $inventory.components | Where-Object name -eq 'Microsoft.NETCore.App') {
    $path = $component.binary_anchors[0].installed_path
    $binaries += Binary $path ([NativeImageInventory]::Read($path)) $component.name $component.version 'existing coreclr anchor; scope exception'
}
$sdk = $inventory.components | Where-Object name -eq '.NET SDK' | Select-Object -First 1
$sdkPath = Split-Path ($sdk.binary_anchors | Where-Object { [IO.Path]::GetFileName($_.installed_path) -eq 'dotnet.dll' } | Select-Object -First 1).installed_path -Parent
$symbols = @(Get-ChildItem -LiteralPath $sdkPath -Recurse -Depth 4 -File -Filter 'Microsoft.DiaSymReader.Native.*.dll')
if ($symbols.Count -gt 128) { throw 'SDK native symbol reader size bound exceeded.' }
foreach ($file in $symbols) { $binaries += Binary $file.FullName ([NativeImageInventory]::Read($file.FullName)) '.NET SDK' $sdk.version 'selected SDK native symbol reader' }

$roots = @()
foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots')) {
    if (Test-Path $key) { $value=(Get-ItemProperty -LiteralPath $key -Name KitsRoot10 -ErrorAction SilentlyContinue).KitsRoot10; if ($value) { $roots += $value } }
}
if (${env:ProgramFiles(x86)}) { $roots += Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10' }
$roots = @($roots | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | ForEach-Object { [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($_)) } | Sort-Object -Unique)
$sdkRecords = @(); $redist = @()
foreach ($root in $roots) {
    $legalPaths = @()
    foreach ($folder in @('Licenses','License')) {
        $folderPath=Join-Path $root $folder
        if (Test-Path -LiteralPath $folderPath -PathType Container) { $legalPaths += @(Get-ChildItem -LiteralPath $folderPath -Recurse -Depth 3 -File | Where-Object { $_.Name -match '(?i)(licen[cs]e|eula|redist|notice)' }) }
    }
    $legalPaths += @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Name -match '(?i)(licen[cs]e|eula|redist|notice)' })
    $redistPath = Join-Path $root 'Redist'
    $candidates = @()
    if (Test-Path -LiteralPath $redistPath -PathType Container) {
        $candidates = @(Get-ChildItem -LiteralPath $redistPath -Recurse -Depth 6 -File -Filter 'd3dcompiler_47.dll')
        $legalPaths += @(Get-ChildItem -LiteralPath $redistPath -Recurse -Depth 3 -File | Where-Object { $_.Name -match '(?i)^(redist|licen[cs]e|eula|notice).*\.(txt|rtf|html|htm)$' })
    }
    $candidateTotal = $candidates.Count
    $candidates = @($candidates | Sort-Object FullName | Select-Object -First 128)
    foreach ($candidate in $candidates) {
        $entry = Binary $candidate.FullName ([NativeImageInventory]::Read($candidate.FullName)) 'Windows SDK REDIST' 'PATH_VERSION_ONLY' 'observed Windows SDK Redist subtree'
        $entry['relative_to_sdk_root'] = [IO.Path]::GetRelativePath($root,$candidate.FullName).Replace('\','/')
        $redist += $entry
    }
    $versions = @();$includes=Join-Path $root 'Include'
    if (Test-Path -LiteralPath $includes -PathType Container) { $versions=@(Get-ChildItem -LiteralPath $includes -Directory | Select-Object -ExpandProperty Name) }
    $legalPaths = @($legalPaths | Sort-Object FullName -Unique)
    $sdkRecords += @{root=$root;include_directory_versions=$versions;redist_discovered=$candidateTotal;redist_count_limited=($candidateTotal -gt 128)
        legal_discovered=$legalPaths.Count;legal_count_limited=($legalPaths.Count -gt 128)
        local_legal_files=@($legalPaths | Select-Object -First 128 | ForEach-Object { Save-Legal $_.FullName })
        scope='Observed SDK tree and accompanying files; no automatic binary-to-EULA version assignment'}
}
foreach ($binary in $binaries | Where-Object name -eq 'D3DCompiler_47_cor3.dll') {
    $binary.sdk_redist_matches = @($redist | Where-Object { $_.sha256 -eq $binary.sha256 -and $_.size_bytes -eq $binary.size_bytes -and $_.pe.Machine -eq $binary.pe.Machine } | ForEach-Object {
        @{installed_path=$_.installed_path;sha256=$_.sha256;file_version=$_.file_version;relative_to_sdk_root=$_.relative_to_sdk_root;comparison='FULL_FILE_SHA256_SIZE_MACHINE_MATCH';legal_acceptance='REVIEW: SDK license/REDIST/Classic Windows conditions are separate'}
    })
}
$result=[ordered]@{schema_version=1;checked_at_utc=[DateTimeOffset]::UtcNow.ToString('o');source_commit=$inventory.source_commit
    workflow_run_id=$inventory.workflow_run_id;workflow_job=$inventory.workflow_job
    policy=$policy;retained_references=$retainedReferences;runtime_discovery=$discovery
    binaries=$binaries;sdk_roots=$sdkRecords;sdk_redist_binaries=$redist
    application_scope=@{project='src/Embasic.ENote.App/Embasic.ENote.App.csproj';target='net10.0-windows';use_wpf=$true;distribution='Not published/installed; runtime inventory does not determine shipped files'}
    legal_acceptance='REVIEW';release_status='BLOCKED'
    preserved='TUR53 contextual/registry/package proofs untouched; this record adds native inventory and legal scope observations only'}
$result | ConvertTo-Json -Depth 40 | Set-Content (Join-Path $output 'native-scope.json') -Encoding utf8
Write-Host "Retained $($binaries.Count) native/mixed and scope-exception records, $($redist.Count) SDK REDIST candidates; legal REVIEW, release BLOCKED."
