$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../tests/Embasic.ENote.WpfAcceptance'
function Get-XpsDriver {
    Get-PrinterDriver | Where-Object Name -Like 'Microsoft XPS Document Writer*' |
        Sort-Object Name -Descending | Select-Object -First 1
}
$driver = Get-XpsDriver
if (-not $driver -and $env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted') {
    # Enable the OS inbox driver on this disposable worker only.
    $featureName = 'Printing-XPSServices-Features'
    $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Continue
    if ($feature -and $feature.State -ne 'Enabled') {
        Write-Host "Enabling Windows optional feature $featureName ($($feature.State))."
        Enable-WindowsOptionalFeature -Online -FeatureName $featureName -All -NoRestart -ErrorAction Continue |
            Format-List FeatureName, State, RestartNeeded
    }
    $driver = Get-XpsDriver
    if (-not $driver) {
        Add-PrinterDriver -Name 'Microsoft XPS Document Writer v4' -ErrorAction Continue
        $driver = Get-XpsDriver
    }
}
if (-not $driver) {
    Get-PrinterDriver | Format-Table Name, PrinterEnvironment
    throw 'BLOCKED: Microsoft XPS Document Writer driver is absent on this Windows runner.'
}
Write-Host "Using installed Windows print driver: $($driver.Name)."
$root = New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) "enote-tur43-$([guid]::NewGuid().ToString('N'))")
try {
    foreach ($scenario in @('portrait-range', 'landscape-current')) {
        $queue = "Embasic ENote TUR43 $($root.Name) $scenario"
        $output = Join-Path $root.FullName "$scenario.oxps"
        try {
            Add-PrinterPort -Name $output
            Add-Printer -Name $queue -DriverName $driver.Name -PortName $output
            dotnet run --project $project --configuration Release --no-build -- --spooler $queue $output $scenario
            if ($LASTEXITCODE -ne 0) { throw "Spooler acceptance failed for $scenario." }
        } finally {
            if (Get-Printer -Name $queue -ErrorAction SilentlyContinue) {
                Get-PrintJob -PrinterName $queue | Remove-PrintJob
                Remove-Printer -Name $queue
            }
            if (Get-PrinterPort -Name $output -ErrorAction SilentlyContinue) {
                Remove-PrinterPort -Name $output
            }
        }
    }
} finally {
    Remove-Item -LiteralPath $root.FullName -Recurse -Force
}
