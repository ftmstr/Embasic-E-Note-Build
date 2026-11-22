[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InventoryDirectory,
    [string]$NugetAssets
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$directory = (Resolve-Path $InventoryDirectory).Path
$inventory = Get-Content (Join-Path $directory 'environment-legal.json') -Raw | ConvertFrom-Json
$trustPath = Join-Path $directory 'windows-trust'
if (Test-Path $trustPath) { throw 'Use a new trust evidence directory.' }
New-Item -ItemType Directory -Path $trustPath | Out-Null
$checkedAt = [DateTimeOffset]::UtcNow
$script:ChainCache = @{}

function Save-Certificate($Certificate) {
    if ($null -eq $Certificate) { return $null }
    $raw = $Certificate.RawData
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)).ToLowerInvariant()
    $name = "certificates/$hash.cer"
    New-Item -ItemType Directory -Force -Path (Join-Path $trustPath 'certificates') | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $trustPath $name), $raw)
    return [ordered]@{ subject=$Certificate.Subject; issuer=$Certificate.Issuer
        thumbprint_sha1=$Certificate.Thumbprint; der_sha256=$hash; evidence_path=$name
        not_before=$Certificate.NotBefore.ToUniversalTime().ToString('o')
        not_after=$Certificate.NotAfter.ToUniversalTime().ToString('o') }
}

function Read-Chain($Certificate) {
    if ($null -eq $Certificate) { return $null }
    if ($script:ChainCache.ContainsKey($Certificate.Thumbprint)) { return $script:ChainCache[$Certificate.Thumbprint] }
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::Offline
        $chain.ChainPolicy.RevocationFlag = [Security.Cryptography.X509Certificates.X509RevocationFlag]::EntireChain
        $chain.ChainPolicy.DisableCertificateDownloads = $true
        $built = $chain.Build($Certificate)
        $elements = @($chain.ChainElements | ForEach-Object { Save-Certificate $_.Certificate })
        $root = if ($chain.ChainElements.Count) { $chain.ChainElements[$chain.ChainElements.Count-1].Certificate } else { $null }
        $stores = @()
        if ($null -ne $root) {
            foreach ($location in @('LocalMachine','CurrentUser')) {
                if (Test-Path "Cert:\$location\Root\$($root.Thumbprint)") { $stores += "$location/Root" }
            }
        }
        $result = [ordered]@{ chain_build_result=$built; revocation_mode='Offline'; certificate_downloads=$false
            status=@($chain.ChainStatus | ForEach-Object { @{status=$_.Status.ToString(); information=$_.StatusInformation.Trim()} })
            elements=$elements; observed_root_stores=$stores
            scope='Observed CI Windows trust store; offline revocation is not fresh revocation or independent release approval' }
        $script:ChainCache[$Certificate.Thumbprint] = $result
        return $result
    } finally { $chain.Dispose() }
}

function Read-FileTrust([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @{path=$Path; status='FILE_NOT_PRESENT'} }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    return [ordered]@{ path=$Path; sha256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        authenticode_os_status=$signature.Status.ToString(); status_message=$signature.StatusMessage
        signature_type=[string]$signature.SignatureType; signer=(Save-Certificate $signature.SignerCertificate)
        timestamp_signer=(Save-Certificate $signature.TimeStamperCertificate)
        signer_chain=(Read-Chain $signature.SignerCertificate)
        timestamp_chain=(Read-Chain $signature.TimeStamperCertificate)
        scope='Platform observation, not a final signer allowlist or distribution license decision' }
}

$binaries = @()
$paths = @($inventory.components | ForEach-Object { $_.binary_anchors } | ForEach-Object { $_.installed_path } | Sort-Object -Unique)
foreach ($path in $paths) { $binaries += Read-FileTrust $path }

$node = @()
$workers = @(Get-CimInstance Win32_Process -Filter "Name = 'Runner.Worker.exe'" | Where-Object ExecutablePath)
$liveNodes = @(Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" | Where-Object ExecutablePath | Select-Object -ExpandProperty ExecutablePath -Unique)
foreach ($worker in $workers) {
    $runnerRoot = Split-Path (Split-Path $worker.ExecutablePath -Parent) -Parent
    foreach ($generation in @('node20','node24')) {
        $nodeRoot = Join-Path $runnerRoot "externals/$generation"
        $exe = Join-Path $nodeRoot 'bin/node.exe'
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
        $info = (& $exe -p 'JSON.stringify({execPath:process.execPath,version:process.version,versions:process.versions,release:process.release})') | ConvertFrom-Json
        $legal = @()
        foreach ($candidate in @('LICENSE','LICENSE.txt','share/doc/node/LICENSE')) {
            $path = Join-Path $nodeRoot $candidate
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $raw = [IO.File]::ReadAllBytes($path)
                $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
                $relative = "node-legal/$generation-$hash.txt"
                New-Item -ItemType Directory -Force -Path (Join-Path $trustPath 'node-legal') | Out-Null
                [IO.File]::WriteAllBytes((Join-Path $trustPath $relative),$raw)
                $legal += @{installed_path=$path; evidence_path=$relative; sha256=$hash}
            }
        }
        $node += [ordered]@{ runner_worker_path=$worker.ExecutablePath
            runner_worker_file_version=(Get-Item $worker.ExecutablePath).VersionInfo.FileVersion
            generation=$generation; runtime_info=$info; binary=(Read-FileTrust $exe); legal_files=$legal
            observed_live_node_paths=$liveNodes
            scope='Runner bundled runtime candidate and self-declared source URLs; per-step runtime selection and binary/source rebuild not proven' }
    }
}

$packages = @()
if ($NugetAssets) {
    $assets = Get-Content $NugetAssets -Raw | ConvertFrom-Json -AsHashtable
    $priorMode = $env:NUGET_CERT_REVOCATION_MODE
    try {
        $env:NUGET_CERT_REVOCATION_MODE = 'offline'
        foreach ($name in ($assets.libraries.Keys | Sort-Object)) {
            $library = $assets.libraries[$name]
            if ($library.type -ne 'package') { continue }
            $id,$version = $name.Split('/')
            $archive = $null
            foreach ($folder in $assets.packageFolders.Keys) {
                $candidate = Join-Path (Join-Path $folder $library.path) "$($id.ToLowerInvariant()).$($version.ToLowerInvariant()).nupkg"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $archive=$candidate; break }
            }
            if (-not $archive) { throw "Missing restored package $name" }
            $text = @(& dotnet nuget verify --all $archive *>&1 | ForEach-Object { "$_" })
            $code = $LASTEXITCODE
            $logName = "nuget-$($id.ToLowerInvariant())-$version.log"
            [IO.File]::WriteAllText((Join-Path $trustPath $logName),($text -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
            $cmsCertificates = @()
            $signatureHash = $null
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                $entry = $zip.GetEntry('.signature.p7s')
                if ($entry) {
                    $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
                    try { $stream.CopyTo($memory);$raw=$memory.ToArray() } finally { $stream.Dispose();$memory.Dispose() }
                    $signatureHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)).ToLowerInvariant()
                    [IO.File]::WriteAllBytes((Join-Path $trustPath "$signatureHash.p7s"),$raw)
                    $cms=[Security.Cryptography.Pkcs.SignedCms]::new();$cms.Decode($raw)
                    $cmsCertificates=@($cms.Certificates | ForEach-Object { @{certificate=(Save-Certificate $_); chain=(Read-Chain $_)} })
                }
            } finally { $zip.Dispose() }
            $packages += [ordered]@{name=$name; path=$archive; archive_sha256=(Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
                nuget_verify_exit_code=$code; verify_log=$logName; revocation_mode='offline'
                signature_present=($null -ne $signatureHash); signature_sha256=$signatureHash; cms_certificates=$cmsCertificates
                acceptance='REVIEW: tool exit status is not fresh revocation, independent signer trust or legal acceptance' }
        }
    } finally { $env:NUGET_CERT_REVOCATION_MODE=$priorMode }
}

$npm = @()
$npmArchive = Join-Path $PSScriptRoot '../third-party/PackageTrust/package-evidence.zip'
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $npmArchive).Path)
function Read-ZipJson($Zip,[string]$Name) {
    $reader=[IO.StreamReader]::new($Zip.GetEntry($Name).Open())
    try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
try {
    $keys = Read-ZipJson $zip 'npm-registry-keys.json'
    foreach ($name in @('buffers','chainsaw','binary','twirp-ts')) {
        $metadata = Read-ZipJson $zip "$name/registry.json"
        foreach ($signature in $metadata.dist.signatures) {
            $key = $keys.keys | Where-Object keyid -eq $signature.keyid | Select-Object -First 1
            $status='KEY_NOT_RETURNED';$expiry='UNKNOWN';$spkiHash=$null
            if ($key) {
                $der=[Convert]::FromBase64String($key.key);$spkiHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($der)).ToLowerInvariant()
                $ecdsa=[Security.Cryptography.ECDsa]::Create()
                try {
                    $read=0;$ecdsa.ImportSubjectPublicKeyInfo($der,[ref]$read)
                    $message=[Text.Encoding]::UTF8.GetBytes("$name@$($metadata.version):$($metadata.dist.integrity)")
                    $match=$ecdsa.VerifyData($message,[Convert]::FromBase64String($signature.sig),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
                    $status=if($match){'CRYPTOGRAPHIC_SIGNATURE_MATCH'}else{'CRYPTOGRAPHIC_SIGNATURE_MISMATCH'}
                } finally { $ecdsa.Dispose() }
                $expiry=if(-not $key.expires){'NO_EXPIRY_DECLARED'}elseif([DateTimeOffset]::Parse($key.expires) -lt $checkedAt){'EXPIRED_AT_REVIEW'}else{'NOT_EXPIRED_AT_REVIEW'}
            }
            $npm += @{name=$name; version=$metadata.version; keyid=$signature.keyid; key_spki_sha256=$spkiHash
                cryptographic_status=$status; key_expires=$key.expires; key_time_status=$expiry
                acceptance='REVIEW: key response via registry TLS; no independent root pin or trusted signing time/publisher proof' }
        }
    }
} finally { $zip.Dispose() }

$scripts = @()
$old = Get-Content (Join-Path $PSScriptRoot '../third-party/BuildProvenance/provenance.json') -Raw | ConvertFrom-Json
$setup = $old.actions | Where-Object repository -eq 'actions/setup-dotnet'
$actionRoot = Join-Path (Split-Path $env:RUNNER_WORKSPACE -Parent) "_actions/$($setup.repository)/$($setup.commit)"
$scripts += Read-FileTrust (Join-Path $actionRoot 'externals/install-dotnet.ps1')
$result=[ordered]@{ schema_version=1; checked_at_utc=$checkedAt.ToString('o'); source_commit=$inventory.source_commit
    workflow_run_id=$env:GITHUB_RUN_ID; workflow_job=$inventory.workflow_job; binaries=$binaries; node=$node
    nuget_packages=$packages; npm_registry_signatures=$npm; external_installer_scripts=$scripts
    release_status='BLOCKED'; scope='Observed CI signatures, certificate chains and declared tool origins; not final distribution acceptance' }
$result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $trustPath 'windows-trust.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Collect-ContextualTrust.ps1') -TrustDirectory $trustPath
& (Join-Path $PSScriptRoot 'Collect-NativeBinaryScope.ps1') -InventoryDirectory $directory
$manifest=@(Get-ChildItem $trustPath -Recurse -File | ForEach-Object { @{path=[IO.Path]::GetRelativePath($directory,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();size_bytes=$_.Length} })
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $directory 'windows-trust-files.json') -Encoding utf8
Write-Host "Retained Windows trust observations for $($binaries.Count) anchors, $($node.Count) Node candidates and $($packages.Count) NuGet packages; release BLOCKED."
