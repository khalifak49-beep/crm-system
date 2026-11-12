<#
  CRM System - database backup (works with every SQL Server edition, including Express:
  it is scheduled with Windows Task Scheduler instead of SQL Server Agent).

  Usage:
    .\Backup-CrmDatabase.ps1 -Type Full                 # daily: full backup + verify + statistics update
    .\Backup-CrmDatabase.ps1 -Type Log                  # every 30 min: transaction log backup (FULL recovery)
    .\Backup-CrmDatabase.ps1 -Type Full -BackupFolder "D:\Backups\CRM" -RetentionDays 30

  Install-CrmOnIIS.ps1 -SetupBackups registers both scheduled tasks automatically.

  Restore (example, see docs/Deployment_IIS.md):
    RESTORE DATABASE CRMDatabase FROM DISK='...FULL...bak' WITH NORECOVERY, REPLACE;
    RESTORE LOG CRMDatabase FROM DISK='...LOG...trn' WITH RECOVERY;   -- repeat for each log, in order
#>
param(
    [ValidateSet("Full", "Log")] [string]$Type = "Full",
    [string]$Server = ".",
    [string]$Database = "CRMDatabase",
    [string]$BackupFolder = "C:\CRM-Backups",
    [int]$RetentionDays = 14
)

$ErrorActionPreference = "Stop"
$logFile = Join-Path $BackupFolder "backup-log.txt"
New-Item -ItemType Directory -Force -Path $BackupFolder | Out-Null

function Write-Log([string]$message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss}  [{1}] {2}" -f (Get-Date), $Type, $message
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

function Invoke-Sql([string]$query) {
    $output = & sqlcmd -S $Server -E -C -I -b -h -1 -W -Q "SET NOCOUNT ON; $query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return ($output -join "`n").Trim()
}

try {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    # The backup file is written by the SQL Server service account, not by this script: grant it Modify on the folder
    $serviceAccount = Invoke-Sql "SELECT TOP 1 service_account FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server (%'"
    if ($serviceAccount) {
        & icacls $BackupFolder /grant "${serviceAccount}:(OI)(CI)M" /Q 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Log "Warning: could not grant $serviceAccount access to $BackupFolder (run as Administrator once)." }
    }
    # Compression is not available on SQL Server Express (EngineEdition 4)
    $edition = Invoke-Sql "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)"
    $compression = if ($edition -eq "4") { "" } else { "COMPRESSION," }

    if ($Type -eq "Full") {
        $file = Join-Path $BackupFolder "${Database}_FULL_$stamp.bak"
        Invoke-Sql "BACKUP DATABASE [$Database] TO DISK = N'$file' WITH INIT, $compression CHECKSUM, NAME = N'$Database full $stamp';" | Out-Null
        Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM;" | Out-Null
        Invoke-Sql "USE [$Database]; EXEC sp_updatestats;" | Out-Null
        Write-Log "Full backup OK and verified: $file ($([math]::Round((Get-Item $file).Length / 1MB, 2)) MB)"
    }
    else {
        $recovery = Invoke-Sql "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$Database'"
        if ($recovery -ne "FULL") { Write-Log "Skipped: recovery model is $recovery (log backups need FULL)."; exit 0 }
        $hasFull = Invoke-Sql "SELECT COUNT(*) FROM msdb.dbo.backupset WHERE database_name = N'$Database' AND type = 'D'"
        if ($hasFull -eq "0") { Write-Log "Skipped: no full backup yet - run -Type Full first."; exit 0 }
        $file = Join-Path $BackupFolder "${Database}_LOG_$stamp.trn"
        Invoke-Sql "BACKUP LOG [$Database] TO DISK = N'$file' WITH INIT, $compression CHECKSUM;" | Out-Null
        Write-Log "Log backup OK: $file"
    }

    # Retention: remove backups older than N days (the newest full backup is always kept)
    $limit = (Get-Date).AddDays(-$RetentionDays)
    $newestFull = Get-ChildItem $BackupFolder -Filter "${Database}_FULL_*.bak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Get-ChildItem $BackupFolder -File | Where-Object {
        ($_.Extension -in ".bak", ".trn") -and $_.LastWriteTime -lt $limit -and $_.FullName -ne $newestFull.FullName
    } | ForEach-Object { Remove-Item $_.FullName -Force; Write-Log "Removed old backup: $($_.Name)" }
}
catch {
    Write-Log "FAILED: $($_.Exception.Message)"
    exit 1
}
