[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$TrustDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$trust = (Resolve-Path $TrustDirectory).Path
$old = Get-Content (Join-Path $trust 'windows-trust.json') -Raw | ConvertFrom-Json
$output = Join-Path $trust 'contextual'
if (Test-Path $output) { throw 'Use a new contextual evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref/*.dll')).FullName) + (Join-Path $PSHOME 'System.Security.Cryptography.Pkcs.dll')
Add-Type -Path (Join-Path $PSScriptRoot 'ContextualTrust.cs') -ReferencedAssemblies $refs
$at = [DateTimeOffset]::UtcNow
[ContextualTrust]::Start($output, $at)
$binaries = @()
foreach ($item in @($old.binaries) + @($old.node | ForEach-Object { $_.binary })) {
    $binaries += [ordered]@{installed_path=$item.path; sha256=$item.sha256
        os_authenticode_status=$item.authenticode_os_status
        cms=[ContextualTrust]::Inspect($item.path, $true)}
}
$packages = @()
$pin = '1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D'
$priorMode = $env:NUGET_CERT_REVOCATION_MODE
try {
    $env:NUGET_CERT_REVOCATION_MODE = 'online'
    foreach ($package in $old.nuget_packages) {
        $log = @(& dotnet nuget verify --all --certificate-fingerprint $pin $package.path *>&1 | ForEach-Object { "$_" })
        $exit = $LASTEXITCODE
        $logName = 'nuget-pinned-online-' + $package.name.Replace('/','-') + '.log'
        [IO.File]::WriteAllText((Join-Path $output $logName), ($log -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
        $packages += [ordered]@{name=$package.name; archive_sha256=$package.archive_sha256
            prior_unpinned_offline_exit=$package.nuget_verify_exit_code; pinned_online_exit=$exit
            fingerprint_sha256=$pin.ToLowerInvariant(); pinned_online_log=$logName
            cms=[ContextualTrust]::Inspect((Join-Path $trust ($package.signature_sha256 + '.p7s')), $false)
            scope='Independent published repository certificate comparison, not package author or legal approval; online tool can use cache'}
    }
} finally { $env:NUGET_CERT_REVOCATION_MODE = $priorMode }
$policy = Get-Content (Join-Path $PSScriptRoot '../third-party/TrustPolicy/published-identities.json') -Raw | ConvertFrom-Json
$result = [ordered]@{schema_version=1; source_commit=$old.source_commit
    workflow_run_id=$old.workflow_run_id; workflow_job=$old.workflow_job; checked_at_utc=$at.ToString('o')
    published_identity_comparisons=$policy; binaries=$binaries; nuget_packages=$packages
    direct_crl_observations=[ContextualTrust]::CollectCrls()
    scope='CMS ExtraStore uses system roots; no roots installed or errors ignored. Historical NoCheck excludes revocation. Direct CRLs have certificate-specific coverage only.'
    release_status='BLOCKED'; legal_applicability='REVIEW'}
$result | ConvertTo-Json -Depth 70 | Set-Content (Join-Path $output 'contextual-trust.json') -Encoding utf8
Write-Host "Retained CMS/timestamp/ExtraStore and bounded online/direct-CRL observations; release BLOCKED."
