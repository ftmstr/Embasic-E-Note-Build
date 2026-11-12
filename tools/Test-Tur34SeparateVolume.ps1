param([Parameter(Mandatory)][string]$HarnessDirectory, [switch]$Tur35)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP ('enote34-volume-' + [Guid]::NewGuid().ToString('N'))
$disk = Join-Path $root 'backup.vhdx'
$instructions = Join-Path $root 'diskpart.txt'
$letter = 90..68 | ForEach-Object { [string][char]$_ } |
    Where-Object { !(Test-Path ("${_}:\")) -and !(Get-Volume -DriveLetter $_ -ErrorAction SilentlyContinue) } |
    Select-Object -First 1
if (!$letter) { throw 'No unused backup volume letter.' }
New-Item -ItemType Directory -Path $root | Out-Null

function Wait-RestoreSignal([string]$Name, $Process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if (Test-Path -LiteralPath (Join-Path $workspace $Name)) { return }
        if ($Process.HasExited) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Restore probe did not reach $Name."
}

function Set-VirtualDiskAttachment([string]$Command) {
    @("select vdisk file=`"$disk`"", $Command, 'exit') |
        Set-Content -LiteralPath $instructions -Encoding ascii
    & diskpart.exe /s $instructions
    if ($LASTEXITCODE -ne 0) { throw "DiskPart $Command failed." }
}

try {
    @(
        "create vdisk file=`"$disk`" maximum=128 type=expandable"
        "select vdisk file=`"$disk`""
        'attach vdisk'
        'create partition primary'
        'format fs=ntfs quick label=ENOTE34'
        "assign letter=$letter"
        'exit'
    ) | Set-Content -LiteralPath $instructions -Encoding ascii
    & diskpart.exe /s $instructions
    if ($LASTEXITCODE -ne 0) { throw 'VHDX provisioning failed.' }
    $volume = Get-Volume -DriveLetter $letter
    if ($volume.FileSystemLabel -ne 'ENOTE34' -or $volume.FileSystem -ne 'NTFS') {
        throw 'Expected the disposable NTFS backup volume.'
    }
    $backup = "${letter}:\snapshot-root"
    New-Item -ItemType Directory -Path $backup | Out-Null
    $workspace = Join-Path $root 'workspace'
    $harness = Join-Path (Resolve-Path -LiteralPath $HarnessDirectory).Path 'Embasic.ENote.Tur28.Tests.dll'
    if ($Tur35) {
        $stdout = Join-Path $root 'restore.out'
        $stderr = Join-Path $root 'restore.err'
        $arguments = '"{0}" tur35-backup "{1}" "{2}"' -f $harness,$workspace,$backup
        $worker = Start-Process -FilePath (Get-Command dotnet -CommandType Application).Source -ArgumentList $arguments `
            -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        try {
            Wait-RestoreSignal 'restore-verified' $worker
            Set-VirtualDiskAttachment 'detach vdisk'
            if (Test-Path -LiteralPath $backup) { throw 'Detached backup path is still available.' }
            New-Item -ItemType File -Path (Join-Path $workspace 'volume-detached') | Out-Null
            Wait-RestoreSignal 'restore-blocked' $worker
            Set-VirtualDiskAttachment 'attach vdisk'
            if (!(Test-Path -LiteralPath $backup)) {
                @("select vdisk file=`"$disk`"", 'select partition 1', "assign letter=$letter", 'exit') |
                    Set-Content -LiteralPath $instructions -Encoding ascii
                & diskpart.exe /s $instructions
                if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $backup)) { throw 'Reattached backup path is unavailable.' }
            }
            New-Item -ItemType File -Path (Join-Path $workspace 'volume-reattached') | Out-Null
            if (!$worker.WaitForExit(30000)) { throw 'Restore retry probe did not exit.' }
            if ($worker.ExitCode -ne 0) { throw 'Detached NTFS volume recovery acceptance failed.' }
        } finally {
            if (!$worker.HasExited) { Stop-Process -InputObject $worker -Force }
            Get-Content -LiteralPath $stdout
            Get-Content -LiteralPath $stderr
        }
    } else {
        & dotnet $harness tur34-backup $workspace $backup
        if ($LASTEXITCODE -ne 0) { throw 'Separate NTFS volume backup acceptance failed.' }
    }
} finally {
    if (Test-Path -LiteralPath $disk) {
        @("select vdisk file=`"$disk`"", 'detach vdisk', 'exit') |
            Set-Content -LiteralPath $instructions -Encoding ascii
        & diskpart.exe /s $instructions | Out-Null
    }
    Remove-Item -LiteralPath $root -Recurse -Force
}
