param([Parameter(Mandatory)][string]$HarnessDirectory)

$ErrorActionPreference = 'Stop'
$service = 'EmbasicENoteTur28-' + [Guid]::NewGuid().ToString('N')
$base = Join-Path $env:ProgramData $service
$private = Join-Path $base 'private'
$public = Join-Path $base 'public'
$runtime = Join-Path $base 'runtime'
$resultDir = Join-Path $base 'result'
$hostPath = (Get-Command dotnet -CommandType Application).Source
$harness = Join-Path $runtime 'Embasic.ENote.Tur28.Tests.dll'
$localService = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-19')
$system = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$saved = [System.Collections.Generic.List[object]]::new()
$created = $false

function Invoke-Harness([string]$Mode) {
    & $hostPath $harness $Mode $private $public $service
    if ($LASTEXITCODE -ne 0) { throw "Harness $Mode failed with exit $LASTEXITCODE." }
}

function Add-ServicePermission([string]$Path, [System.Security.AccessControl.FileSystemRights]$Rights) {
    $acl = Get-Acl -LiteralPath $Path
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $localService, $Rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

try {
    New-Item -ItemType Directory -Path $private,$public,$runtime,$resultDir -Force | Out-Null
    Copy-Item -Path (Join-Path $HarnessDirectory '*') -Destination $runtime -Recurse
    Add-ServicePermission $base 'ReadAndExecute'
    Add-ServicePermission $runtime 'ReadAndExecute'
    Add-ServicePermission $public 'ReadAndExecute'
    Add-ServicePermission $resultDir 'Modify'
    Invoke-Harness 'setup'

    $entries = @(Get-ChildItem -LiteralPath $private -Force -Recurse | Sort-Object { $_.FullName.Length } -Descending)
    $entries += Get-Item -LiteralPath $private
    foreach ($entry in $entries) {
        $old = Get-Acl -LiteralPath $entry.FullName
        $saved.Add(@{ Path = $entry.FullName; Acl = $old })
        $acl = Get-Acl -LiteralPath $entry.FullName
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($existing in @($acl.Access)) { $acl.RemoveAccessRuleSpecific($existing) }
        $inherit = if ($entry.PSIsContainer) { 'ContainerInherit,ObjectInherit' } else { 'None' }
        foreach ($sid in @($localService,$system)) {
            $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
                $sid, 'FullControl', $inherit, 'None', 'Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $entry.FullName -AclObject $acl
    }

    $binary = '"{0}" "{1}" service "{2}" "{3}" "{4}"' -f $hostPath,$harness,$private,$public,$service
    & sc.exe create $service binPath= $binary start= demand obj= 'NT AUTHORITY\LocalService'
    if ($LASTEXITCODE -ne 0) { throw 'Disposable service registration failed.' }
    $created = $true
    & sc.exe start $service
    if ($LASTEXITCODE -ne 0) { throw 'Disposable service start failed.' }
    Invoke-Harness 'client'
    $result = Join-Path $resultDir 'service-result.txt'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-Path -LiteralPath $result) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    $receipt = Get-Content -LiteralPath $result -Raw
    if ($receipt.Trim() -ne 'PASS LocalService identity served requests') { throw "Service did not complete: $receipt" }
    Write-Output $receipt
    Write-Output '4/4 Tur28 disposable SCM and distinct-account ACL checks passed.'
}
finally {
    if ($created) {
        & sc.exe stop $service | Out-Null
        & sc.exe delete $service | Out-Null
    }
    for ($i = $saved.Count - 1; $i -ge 0; $i--) {
        Set-Acl -LiteralPath $saved[$i].Path -AclObject $saved[$i].Acl
    }
    if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force }
}
