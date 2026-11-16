param([string]$PdfVerifierPython)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../tests/Embasic.ENote.WpfAcceptance'
$pdf = -not [string]::IsNullOrWhiteSpace($PdfVerifierPython)
$driverName = if ($pdf) { 'Microsoft Print to PDF' } else { 'Microsoft XPS Document Writer v4' }
$driverPattern = if ($pdf) { $driverName } else { 'Microsoft XPS Document Writer*' }
$extension = if ($pdf) { 'pdf' } else { 'oxps' }
$round = if ($pdf) { '44' } else { '43' }
function Get-TestDriver {
    Get-PrinterDriver | Where-Object Name -Like $driverPattern |
        Sort-Object Name -Descending | Select-Object -First 1
}
$driver = Get-TestDriver
$hosted = $env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted'
if (-not $driver -and $hosted) {
    # Enable the OS inbox driver on this disposable worker only.
    $featureName = if ($pdf) { 'Printing-PrintToPDFServices-Features' } else { 'Printing-XPSServices-Features' }
    $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Continue
    if ($feature -and $feature.State -ne 'Enabled') {
        Write-Host "Enabling Windows optional feature $featureName ($($feature.State))."
        Enable-WindowsOptionalFeature -Online -FeatureName $featureName -All -NoRestart -ErrorAction Continue |
            Format-List FeatureName, State, RestartNeeded
    }
    $driver = Get-TestDriver
    if (-not $driver) {
        Add-PrinterDriver -Name $driverName -ErrorAction Continue
        $driver = Get-TestDriver
    }
}
if (-not $driver) {
    Get-PrinterDriver | Format-Table Name, PrinterEnvironment
    throw "BLOCKED: $driverName is absent on this Windows runner."
}
# A local file port is named by a full Windows path. Use the port monitor
# directly to remove it without passing that path to printer-name validation.
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class EnoteTestLocalPort
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterDefaults
    {
        public IntPtr DataType;
        public IntPtr DevMode;
        public uint DesiredAccess;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, ref PrinterDefaults defaults);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool XcvData(IntPtr handle, string command, byte[] input, uint inputLength,
        [Out] byte[] output, uint outputLength, out uint needed, out uint status);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    public static void Delete(string name)
    {
        const uint ServerAccessAdminister = 1;
        var defaults = new PrinterDefaults { DesiredAccess = ServerAccessAdminister };
        if (!OpenPrinter(",XcvMonitor Local Port", out var handle, ref defaults))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            byte[] input = Encoding.Unicode.GetBytes(name + "\0");
            byte[] output = new byte[4];
            if (!XcvData(handle, "DeletePort", input, (uint)input.Length, output, (uint)output.Length,
                         out _, out uint status))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status != 0) throw new Win32Exception((int)status);
        }
        finally
        {
            ClosePrinter(handle);
        }
    }
}
'@

function Remove-TestFilePort([string]$name) {
    $phases = if ($hosted) { 2 } else { 1 }
    for ($phase = 0; $phase -lt $phases; $phase++) {
        for ($attempt = 0; $attempt -lt 12; $attempt++) {
            if (-not (Get-PrinterPort | Where-Object Name -EQ $name)) { return }
            try {
                [EnoteTestLocalPort]::Delete($name)
            } catch {
                $lastError = $_
            }
            Start-Sleep -Milliseconds 500
        }
        if ($phase -eq 0 -and $hosted -and (Get-PrinterPort | Where-Object Name -EQ $name)) {
            Write-Host 'Windows still holds the disposable port. Restarting the spooler on the dedicated hosted worker.'
            Restart-Service -Name Spooler -Force
            (Get-Service -Name Spooler).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
        }
    }
    if (Get-PrinterPort | Where-Object Name -EQ $name) {
        throw "The disposable local file port still exists after cleanup. $lastError"
    }
}

Write-Host "Using installed Windows print driver: $($driver.Name)."
$root = New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) "enote-tur$round-$([guid]::NewGuid().ToString('N'))")
try {
    foreach ($scenario in @('portrait-range', 'landscape-current')) {
        $queue = "Embasic ENote TUR$round $($root.Name) $scenario"
        $output = Join-Path $root.FullName "$scenario.$extension"
        try {
            Add-PrinterPort -Name $output
            Add-Printer -Name $queue -DriverName $driver.Name -PortName $output
            if ($pdf -and $scenario -eq 'portrait-range') {
                dotnet run --project $project --configuration Release --no-build -- --pdf-cancel $queue $output
                if ($LASTEXITCODE -ne 0) { throw 'PDF cancellation acceptance failed.' }
            }
            dotnet run --project $project --configuration Release --no-build -- --spooler $queue $output $scenario
            if ($LASTEXITCODE -ne 0) { throw "Spooler acceptance failed for $scenario." }
            if ($pdf) {
                & $PdfVerifierPython (Join-Path $PSScriptRoot 'Verify-Tur44DriverPdf.py') $output $scenario
                if ($LASTEXITCODE -ne 0) { throw "PDF verification failed for $scenario." }
            }
        } finally {
            if (Get-Printer -Name $queue -ErrorAction SilentlyContinue) {
                Get-PrintJob -PrinterName $queue | Remove-PrintJob
                if (Get-PrinterPort | Where-Object Name -EQ 'NUL:') {
                    Set-Printer -Name $queue -PortName 'NUL:'
                }
                Remove-Printer -Name $queue
            }
            Remove-TestFilePort $output
            if (Get-Printer -Name $queue -ErrorAction SilentlyContinue) {
                throw 'The disposable print queue still exists after cleanup.'
            }
            Write-Host "Removed the disposable queue and file port for $scenario."
        }
    }
} finally {
    Remove-Item -LiteralPath $root.FullName -Recurse -Force
}
