param([Parameter(Mandatory)][string]$HarnessDirectory, [switch]$Dedicated, [switch]$CngRotation)
if ($CngRotation) { $Dedicated = $true }

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
$peer = $service + "-peer"
$peerCreated = $false
$cngStarted = $false

function Invoke-Harness([string]$Mode) {
    if ($Dedicated) { & $hostPath $harness $Mode $private $public $service $serviceSid.Value $clientSid.Value }
    else { & $hostPath $harness $Mode $private $public $service }
    if ($LASTEXITCODE -ne 0) { throw "Harness $Mode failed with exit $LASTEXITCODE." }
}

function Invoke-StandardClient([string]$Mode) {
    $clientArgs = '"{0}" {1} "{2}" "{3}" "{4}" "{5}" "{6}"' -f $harness,$Mode,$private,$public,$service,$serviceSid.Value,$clientSid.Value
    $stdout = Join-Path $resultDir "$Mode-output.txt"
    $stderr = Join-Path $resultDir "$Mode-error.txt"
    $process = Start-Process -FilePath $hostPath -ArgumentList $clientArgs -Credential $credential -WorkingDirectory $runtime -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru
    Get-Content -LiteralPath $stdout
    if ($process.ExitCode -ne 0) { throw "Standard client $Mode failed: $(Get-Content -LiteralPath $stderr -Raw)" }
}

function Wait-Receipt([string]$Name, [string]$Expected) {
    $path = Join-Path $resultDir $Name
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    while (!(Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    $receipt = Get-Content -LiteralPath $path -Raw
    if ($receipt.Trim() -ne $Expected) { throw "Service did not complete: $receipt" }
    Write-Output "$Name $($receipt.Trim())"
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
    if ($CngRotation) {
        $cngStarted = $true
        Invoke-Harness 'cng-setup'
    } else { Invoke-Harness 'setup' }

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
        if ($CngRotation -and $entry.FullName -eq (Join-Path $private 'publisher-2.bin')) {
            $runnerSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
            $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($runnerSid, 'Read', 'Allow'))
        }
        Set-Acl -LiteralPath $entry.FullName -AclObject $acl
    }

    $mode = if ($CngRotation) { 'cng-service' } else { 'service' }
    $binary = '"{0}" "{1}" {2} "{3}" "{4}" "{5}"' -f $hostPath,$harness,$mode,$private,$public,$service
    if ($Dedicated) { $binary += ' "{0}" "{1}"' -f $serviceSid.Value,$clientSid.Value }
    $initialBinary = if ($CngRotation) { $binary + ' 1' } else { $binary }
    & sc.exe create $service binPath= $initialBinary start= demand obj= 'NT AUTHORITY\LocalService'
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
    if ($CngRotation) {
        $peerText = (& sc.exe showsid $peer | Out-String)
        if ($LASTEXITCODE -ne 0 -or $peerText -notmatch 'S-1-5-80-(?:[0-9]+-){4}[0-9]+') { throw 'Peer SID lookup failed.' }
        $peerSid = [System.Security.Principal.SecurityIdentifier]::new($Matches[0])
        Add-Permission $runtime 'ReadAndExecute' $peerSid
        Add-Permission $resultDir 'Modify' $peerSid
        $peerBinary = '"{0}" "{1}" cng-peer "{2}" "{3}" "{4}" "{5}" "{6}" "{7}" "{8}"' -f $hostPath,$harness,$private,$public,$service,$serviceSid.Value,$clientSid.Value,$peer,$peerSid.Value
        & sc.exe create $peer binPath= $peerBinary start= demand obj= 'NT AUTHORITY\LocalService'
        if ($LASTEXITCODE -ne 0) { throw 'Peer service registration failed.' }
        $peerCreated = $true
        & sc.exe sidtype $peer unrestricted
        if ($LASTEXITCODE -ne 0) { throw 'Peer service SID setup failed.' }
        & sc.exe start $peer
        if ($LASTEXITCODE -ne 0) { throw 'Peer service start failed.' }
        Wait-Receipt 'peer.txt' 'PASS'

        Invoke-StandardClient 'cng-before'
        (Get-Service -Name $service).WaitForStatus('Running', [TimeSpan]::FromSeconds(10))
        # The public fixture directory is writable by the trusted runner, read-only for the service and reader.
        [IO.File]::WriteAllText((Join-Path $public 'epoch-1.stop'), 'retire-epoch-1')
        Wait-Receipt 'epoch-1.txt' 'PASS protected retirement observed'
        (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        Invoke-StandardClient 'cng-offline'

        & sc.exe config $service binPath= ($binary + ' 2')
        if ($LASTEXITCODE -ne 0) { throw 'Second key epoch service config failed.' }
        & sc.exe start $service
        if ($LASTEXITCODE -ne 0) { throw 'Second key epoch service start failed.' }
        Invoke-StandardClient 'cng-after'
        Invoke-Harness 'cng-publish'
        Wait-Receipt 'epoch-2.txt' 'PASS'
        Write-Output '7/7 Tur30 persisted KSP, peer LocalService, shutdown, rotation and signed publisher checks passed.'
    } else {
        if ($Dedicated) { Invoke-StandardClient 'client' } else { Invoke-Harness 'client' }
        $expectedReceipt = if ($Dedicated) { 'PASS dedicated service SID served rotated endpoint' } else { 'PASS LocalService identity served requests' }
        Wait-Receipt 'service-result.txt' $expectedReceipt
        if ($Dedicated) { Write-Output '7/7 Tur29 service SID, standard-user ACL, SCM and rotated pin checks passed.' }
        else { Write-Output '4/4 Tur28 disposable SCM and distinct-account ACL checks passed.' }
    }
}
catch {
    if ($CngRotation) {
        foreach ($file in @('epoch-1.txt','epoch-2.txt','peer.txt')) {
            $path = Join-Path $resultDir $file
            if (Test-Path -LiteralPath $path) { Write-Output "$file $((Get-Content -LiteralPath $path -Raw).Trim())" }
        }
    }
    throw
}
finally {
    if ($peerCreated) {
        & sc.exe stop $peer | Out-Null
        & sc.exe delete $peer | Out-Null
    }
    if ($created) {
        & sc.exe stop $service | Out-Null
        & sc.exe delete $service | Out-Null
    }
    if ($cngStarted) { Invoke-Harness 'cng-cleanup' }
    if ($userName) { Remove-LocalUser -Name $userName }
    for ($i = $saved.Count - 1; $i -ge 0; $i--) {
        Set-Acl -LiteralPath $saved[$i].Path -AclObject $saved[$i].Acl
    }
    if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force }
}
