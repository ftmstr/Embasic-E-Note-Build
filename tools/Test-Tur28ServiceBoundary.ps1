param([Parameter(Mandatory)][string]$HarnessDirectory, [switch]$Dedicated)

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
$userName = $null
$credential = $null
$clientSid = $null
$serviceSid = $localService

function Invoke-Harness([string]$Mode) {
    if ($Dedicated) { & $hostPath $harness $Mode $private $public $service $serviceSid.Value $clientSid.Value }
    else { & $hostPath $harness $Mode $private $public $service }
    if ($LASTEXITCODE -ne 0) { throw "Harness $Mode failed with exit $LASTEXITCODE." }
}

function Add-Permission([string]$Path, [System.Security.AccessControl.FileSystemRights]$Rights, $Identity) {
    $acl = Get-Acl -LiteralPath $Path
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $Identity, $Rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

try {
    New-Item -ItemType Directory -Path $private,$public,$runtime,$resultDir -Force | Out-Null
    Copy-Item -Path (Join-Path $HarnessDirectory '*') -Destination $runtime -Recurse
    if ($Dedicated) {
        $candidateUser = 'enote29-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
        $password = ConvertTo-SecureString ('A9!' + [Guid]::NewGuid().ToString('N')) -AsPlainText -Force
        $user = New-LocalUser -Name $candidateUser -Password $password
        $userName = $candidateUser
        $users = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
        if (!(@(Get-LocalGroupMember -SID $users).SID.Value -contains $user.SID.Value)) {
            Add-LocalGroupMember -SID $users -Member $user
        }
        $clientSid = $user.SID
        $credential = [PSCredential]::new("$env:COMPUTERNAME\$userName", $password)
        $sidText = (& sc.exe showsid $service | Out-String)
        if ($LASTEXITCODE -ne 0 -or $sidText -notmatch 'S-1-5-80-(?:[0-9]+-){4}[0-9]+') { throw 'Service SID lookup failed.' }
        $serviceSid = [System.Security.Principal.SecurityIdentifier]::new($Matches[0])
    }
    Add-Permission $base 'ReadAndExecute' $serviceSid
    Add-Permission $runtime 'ReadAndExecute' $serviceSid
    Add-Permission $public 'ReadAndExecute' $serviceSid
    Add-Permission $resultDir 'Modify' $serviceSid
    if ($Dedicated) {
        Add-Permission $base 'ReadAndExecute' $clientSid
        Add-Permission $runtime 'ReadAndExecute' $clientSid
        $endpointAcl = Get-Acl -LiteralPath $public
        $endpointAcl.SetAccessRuleProtection($true, $false)
        foreach ($existing in @($endpointAcl.Access)) { $endpointAcl.RemoveAccessRuleSpecific($existing) }
        foreach ($sid in @($system, [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'),
            [System.Security.Principal.WindowsIdentity]::GetCurrent().User, $serviceSid, $clientSid)) {
            $rights = if ($sid -eq $serviceSid -or $sid -eq $clientSid) { 'ReadAndExecute' } else { 'FullControl' }
            $endpointAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
                $sid, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        Set-Acl -LiteralPath $public -AclObject $endpointAcl
    }
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
        foreach ($sid in @($serviceSid,$system)) {
            $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
                $sid, 'FullControl', $inherit, 'None', 'Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $entry.FullName -AclObject $acl
    }

    $binary = '"{0}" "{1}" service "{2}" "{3}" "{4}"' -f $hostPath,$harness,$private,$public,$service
    if ($Dedicated) { $binary += ' "{0}" "{1}"' -f $serviceSid.Value,$clientSid.Value }
    & sc.exe create $service binPath= $binary start= demand obj= 'NT AUTHORITY\LocalService'
    if ($LASTEXITCODE -ne 0) { throw 'Disposable service registration failed.' }
    $created = $true
    if ($Dedicated) {
        & sc.exe sidtype $service unrestricted
        if ($LASTEXITCODE -ne 0) { throw 'Disposable service SID setup failed.' }
        & sc.exe sdset $service "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;LC;;;$($clientSid.Value))"
        if ($LASTEXITCODE -ne 0) { throw 'Disposable SCM DACL setup failed.' }
    }
    & sc.exe start $service
    if ($LASTEXITCODE -ne 0) { throw 'Disposable service start failed.' }
    if ($Dedicated) {
        $clientArgs = '"{0}" client "{1}" "{2}" "{3}" "{4}" "{5}"' -f $harness,$private,$public,$service,$serviceSid.Value,$clientSid.Value
        $stdout = Join-Path $resultDir 'client-output.txt'
        $stderr = Join-Path $resultDir 'client-error.txt'
        $process = Start-Process -FilePath $hostPath -ArgumentList $clientArgs -Credential $credential -WorkingDirectory $runtime -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru
        Get-Content -LiteralPath $stdout
        if ($process.ExitCode -ne 0) { throw "Standard client failed: $(Get-Content -LiteralPath $stderr -Raw)" }
    } else { Invoke-Harness 'client' }
    $result = Join-Path $resultDir 'service-result.txt'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-Path -LiteralPath $result) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    $receipt = Get-Content -LiteralPath $result -Raw
    $expectedReceipt = if ($Dedicated) { 'PASS dedicated service SID served rotated endpoint' } else { 'PASS LocalService identity served requests' }
    if ($receipt.Trim() -ne $expectedReceipt) { throw "Service did not complete: $receipt" }
    Write-Output $receipt
    if ($Dedicated) { Write-Output '7/7 Tur29 service SID, standard-user ACL, SCM and rotated pin checks passed.' }
    else { Write-Output '4/4 Tur28 disposable SCM and distinct-account ACL checks passed.' }
}
finally {
    if ($created) {
        & sc.exe stop $service | Out-Null
        & sc.exe delete $service | Out-Null
    }
    if ($userName) { Remove-LocalUser -Name $userName }
    for ($i = $saved.Count - 1; $i -ge 0; $i--) {
        Set-Acl -LiteralPath $saved[$i].Path -AclObject $saved[$i].Acl
    }
    if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force }
}
