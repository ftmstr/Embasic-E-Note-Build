param([Parameter(Mandatory)][string]$HarnessDirectory, [switch]$Dedicated, [switch]$CngRotation, [switch]$Hardened, [switch]$Tur32, [switch]$Tur33)
if ($Tur33) { $Tur32 = $true }
if ($Tur32) { $Hardened = $true }
if ($Hardened) { $CngRotation = $true }
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
$hardeningFlag = @()
if ($Hardened) { $hardeningFlag = @('--hardened') }
if ($Tur32) { $hardeningFlag = @('--tur32') }
if ($Tur33) { $hardeningFlag = @('--tur33') }

function Invoke-Harness([string]$Mode) {
    if ($Dedicated) { & $hostPath $harness $Mode $private $public $service $serviceSid.Value $clientSid.Value @hardeningFlag }
    else { & $hostPath $harness $Mode $private $public $service }
    if ($LASTEXITCODE -ne 0) { throw "Harness $Mode failed with exit $LASTEXITCODE." }
}

function Invoke-StandardClient([string]$Mode) {
    $clientArgs = '"{0}" {1} "{2}" "{3}" "{4}" "{5}" "{6}"' -f $harness,$Mode,$private,$public,$service,$serviceSid.Value,$clientSid.Value
    if ($Hardened) { $clientArgs += ' ' + $hardeningFlag[0] }
    $stdout = Join-Path $resultDir "$Mode-output.txt"
    $stderr = Join-Path $resultDir "$Mode-error.txt"
    $process = Start-Process -FilePath $hostPath -ArgumentList $clientArgs -Credential $credential -WorkingDirectory $runtime -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru
    Get-Content -LiteralPath $stdout
    if ($process.ExitCode -ne 0) { throw "Standard client $Mode failed: $(Get-Content -LiteralPath $stderr -Raw)" }
}

function Wait-Receipt([string]$Name, [string]$Expected) {
    $path = Join-Path $resultDir $Name
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    $receipt = ''
    do {
        if (Test-Path -LiteralPath $path) {
            try { $receipt = [IO.File]::ReadAllText($path) } catch [System.IO.IOException] { }
        }
        if ([string]::IsNullOrWhiteSpace($receipt)) { Start-Sleep -Milliseconds 100 }
    } while ([string]::IsNullOrWhiteSpace($receipt) -and [DateTime]::UtcNow -lt $deadline)
    if ([string]::IsNullOrWhiteSpace($receipt)) { throw "Service receipt $Name did not arrive." }
    if ($receipt.Trim() -ne $Expected) { throw "Service did not complete: $receipt" }
    Write-Output "$Name $($receipt.Trim())"
}

function Publish-RetirementRequest {
    $request = Join-Path $public 'epoch-1.stop'
    $pending = "$request.pending"
    [IO.File]::WriteAllText($pending, 'retire-epoch-1')
    [IO.File]::Move($pending, $request)
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
        if (($CngRotation -and $entry.FullName -eq (Join-Path $private 'publisher-2.bin')) -or
            ($Hardened -and $entry.FullName -eq (Join-Path $private 'reader-2.bin'))) {
            $runnerSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
            $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($runnerSid, 'Read', 'Allow'))
        }
        Set-Acl -LiteralPath $entry.FullName -AclObject $acl
    }

    $mode = if ($CngRotation) { 'cng-service' } else { 'service' }
    $binary = '"{0}" "{1}" {2} "{3}" "{4}" "{5}"' -f $hostPath,$harness,$mode,$private,$public,$service
    if ($Dedicated) { $binary += ' "{0}" "{1}"' -f $serviceSid.Value,$clientSid.Value }
    if ($Hardened) { $binary += ' ' + $hardeningFlag[0] }
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
        if ($Hardened) {
            $pidFile = Join-Path $resultDir 'epoch-1.pid'
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            while (!(Test-Path -LiteralPath $pidFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
            $targetPid = [int](Get-Content -LiteralPath $pidFile -Raw)
            Get-Process -Id $targetPid -ErrorAction Stop | Out-Null
            $peerBinary += " $targetPid " + $hardeningFlag[0]
        }
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
        Publish-RetirementRequest
        Wait-Receipt 'epoch-1.txt' 'PASS protected retirement observed'
        (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        if ($Hardened) {
            & sc.exe config $service binPath= ($binary + ' 0')
            if ($LASTEXITCODE -ne 0) { throw 'Wrong KSP test configuration failed.' }
            & sc.exe start $service
            if ($LASTEXITCODE -ne 0) { throw 'Wrong KSP test did not start the process.' }
            Wait-Receipt 'bad-key.txt' 'PASS wrong KSP rejected before endpoint creation'
            (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        }
        Invoke-StandardClient 'cng-offline'

        if ($Tur32) {
            Remove-Item -LiteralPath (Join-Path $public 'epoch-1.stop'),(Join-Path $resultDir 'epoch-1.txt')
            & sc.exe config $service binPath= ($binary + ' 1')
            if ($LASTEXITCODE -ne 0) { throw 'Approved prior service configuration failed.' }
            & sc.exe start $service
            if ($LASTEXITCODE -ne 0) { throw 'Approved prior service start failed.' }
            Invoke-StandardClient 'cng-before'
            Publish-RetirementRequest
            Wait-Receipt 'epoch-1.txt' 'PASS protected retirement observed'
            (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
            Write-Output 'PASS approved prior endpoint restarted before new pin selection'
        }

        & sc.exe config $service binPath= ($binary + ' 2')
        if ($LASTEXITCODE -ne 0) { throw 'Second key epoch service config failed.' }
        & sc.exe start $service
        if ($LASTEXITCODE -ne 0) { throw 'Second key epoch service start failed.' }
        if ($Hardened) {
            Invoke-StandardClient 'cng-pin-gap'
            $pendingReader = Join-Path $public 'reader-2.pending'
            $activeReader = Join-Path $public 'reader-2.bin'
            if ($Tur32) {
                if ($Tur33) {
                    [IO.File]::Copy((Join-Path $public 'reader-1.bin'), $activeReader)
                    Invoke-StandardClient 'cng-reject-envelope'
                }
                $wrong = [IO.File]::ReadAllBytes((Join-Path $private 'reader-2.bin'))
                $wrong[0] = $wrong[0] -bxor 1
                [IO.File]::WriteAllBytes($activeReader, $wrong)
                Invoke-StandardClient 'cng-reject-envelope'
            }
            [IO.File]::Copy((Join-Path $private 'reader-2.bin'), $pendingReader)
            [IO.File]::Move($pendingReader, $activeReader, $Tur32.IsPresent)
        }
        Invoke-StandardClient 'cng-after'
        Invoke-Harness 'cng-publish'
        Wait-Receipt 'epoch-2.txt' 'PASS'
        if ($Tur33) {
            (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
            [IO.File]::WriteAllText((Join-Path $public 'selected-epoch'), '2')
            & sc.exe config $service binPath= ($binary + ' 1')
            if ($LASTEXITCODE -ne 0) { throw 'Stale service configuration test failed.' }
            & sc.exe start $service
            if ($LASTEXITCODE -ne 0) { throw 'Stale process did not reach the activation gate.' }
            Wait-Receipt 'rollback-blocked.txt' 'PASS stale service rejected after new epoch selection'
            (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
            Invoke-StandardClient 'cng-offline'
            Write-Output '5/5 Tur33 recipient key rotation, unauthorized distributor and postselection rollback checks passed.'
        }
        elseif ($Tur32) { Write-Output '6/6 Tur32 wrapped roles, distributor boundary, rejected envelope and approved fallback checks passed.' }
        elseif ($Hardened) { Write-Output '6/6 Tur31 process DACL, role custody, wrong KSP and interrupted pin distribution checks passed.' }
        else { Write-Output '7/7 Tur30 persisted KSP, peer LocalService, shutdown, rotation and signed publisher checks passed.' }
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
        foreach ($file in @('epoch-1.txt','epoch-2.txt','peer.txt','bad-key.txt')) {
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
