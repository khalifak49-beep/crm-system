<#
  CRM System - one-shot IIS installer. Run in an ELEVATED PowerShell (Run as Administrator):

    powershell -ExecutionPolicy Bypass -File .\Deploy\Install-CrmOnIIS.ps1 -HostName crm.company.local -SetupBackups

  What it does (safe to run again - every step checks before it changes anything):
    1. Enables IIS (Windows Server: Install-WindowsFeature / Windows 10-11 Pro: Enable-WindowsOptionalFeature)
       including WebSockets + Application Initialization.
    2. Checks the ASP.NET Core Module V2 (.NET 10 Hosting Bundle) - stops with the download link if missing.
    3. Publishes the app (if -SkipPublish is not given) and copies it to -SitePath,
       keeping App_Data, logs and wwwroot\uploads from the previous deployment.
    4. Creates the application pool (No Managed Code, AlwaysRunning, no idle timeout, no periodic recycle)
       so the reminder engine keeps running 24/7.
    5. Creates the IIS site + binding, preload enabled.
    6. Grants the app pool identity Modify rights on App_Data, logs and wwwroot\uploads only.
    7. Runs Database\07_ProductionSetup.sql: SQL login for the app pool (db_datareader/db_datawriter only),
       FULL recovery.
    8. Opens the firewall port, optionally registers the backup scheduled tasks, warms up /health.

  Note: Windows 11 HOME does not include IIS. Use Windows Server or Windows 10/11 Pro/Enterprise.
#>
param(
    [string]$SiteName = "CrmSystem",
    [string]$HostName = "",
    [int]$Port = 80,
    [string]$SitePath = "C:\inetpub\CrmSystem",
    [string]$SqlServer = ".",
    [string]$Database = "CRMDatabase",
    [string]$PublicUrl = "",
    [string]$AdminPassword = "",
    [string]$BackupFolder = "C:\CRM-Backups",
    [switch]$SetupBackups,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\CrmSystem"
$appPoolIdentity = "IIS APPPOOL\$SiteName"

function Step([string]$text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "    OK  $text" -ForegroundColor Green }
function Warn([string]$text) { Write-Host "    !!  $text" -ForegroundColor Yellow }

# ---------- 0. Pre-checks ----------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script in PowerShell opened with 'Run as Administrator'."
}
$os = Get-CimInstance Win32_OperatingSystem
if ($os.Caption -match "Home") {
    throw "$($os.Caption) does not include IIS. Use Windows Server or Windows 10/11 Pro/Enterprise."
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found. Install the SQL Server command line tools." }

# ---------- 1. IIS features ----------
Step "Enabling IIS features"
$isServer = $os.ProductType -ne 1
if ($isServer) {
    $features = "Web-Server", "Web-Default-Doc", "Web-Static-Content", "Web-Http-Errors", "Web-Http-Logging",
                "Web-Stat-Compression", "Web-Dyn-Compression", "Web-Filtering", "Web-WebSockets", "Web-AppInit", "Web-Mgmt-Console"
    $result = Install-WindowsFeature -Name $features
    if ($result.RestartNeeded -eq "Yes") { Warn "A restart is required. Restart Windows, then run this script again."; exit 1 }
}
else {
    $features = "IIS-WebServerRole", "IIS-WebServer", "IIS-CommonHttpFeatures", "IIS-DefaultDocument", "IIS-StaticContent",
                "IIS-HttpErrors", "IIS-HttpLogging", "IIS-HttpCompressionStatic", "IIS-HttpCompressionDynamic",
                "IIS-RequestFiltering", "IIS-WebSockets", "IIS-ApplicationInit", "IIS-ManagementConsole"
    foreach ($feature in $features) {
        $state = Get-WindowsOptionalFeature -Online -FeatureName $feature
        if ($state.State -ne "Enabled") {
            $r = Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart
            if ($r.RestartNeeded) { Warn "$feature needs a restart." }
        }
    }
}
Ok "IIS is enabled"
Import-Module WebAdministration

# ---------- 2. ASP.NET Core Module ----------
Step "Checking ASP.NET Core Module V2 (.NET 10 Hosting Bundle)"
$ancm = Join-Path $env:ProgramFiles "IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
if (-not (Test-Path $ancm)) {
    Warn "The .NET 10 Hosting Bundle is not installed."
    Warn "Download 'Hosting Bundle' from https://dotnet.microsoft.com/download/dotnet/10.0 , install it, then run this script again."
    exit 1
}
$runtimes = & dotnet --list-runtimes 2>$null
if (-not ($runtimes -match "Microsoft.AspNetCore.App 10\.")) { throw "ASP.NET Core 10 runtime not found. Reinstall the .NET 10 Hosting Bundle." }
Ok "ASP.NET Core Module V2 + runtime 10 found"

# ---------- 3. Publish + copy ----------
if (-not $SkipPublish) {
    Step "Publishing the application (Release)"
    & dotnet publish (Join-Path $root "CrmSystem\CrmSystem.csproj") -p:PublishProfile=IIS-Folder -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
if (-not (Test-Path (Join-Path $publishDir "CrmSystem.dll"))) { throw "Publish output not found in $publishDir" }

Step "Copying files to $SitePath"
if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
    # app_offline.htm makes ANCM shut the app down cleanly so files are not locked
    Set-Content -Path (Join-Path $SitePath "app_offline.htm") -Value "<html><body><h2>Updating the CRM system... / جاري تحديث النظام</h2></body></html>" -Encoding UTF8
    Start-Sleep -Seconds 3
}
New-Item -ItemType Directory -Force -Path $SitePath | Out-Null
# /XD keeps runtime data folders (App_Data, logs, wwwroot\uploads) from the previous deployment
$robo = & robocopy $publishDir $SitePath /E /NFL /NDL /NJH /NJS /NP /R:3 /W:2 `
    /XD App_Data logs uploads /XF app_offline.htm appsettings.Production.json
# appsettings.Production.json is copied only on the first install, so server edits (PublicUrl, connection string) survive updates
if (-not (Test-Path (Join-Path $SitePath "appsettings.Production.json"))) {
    Copy-Item (Join-Path $publishDir "appsettings.Production.json") $SitePath
}
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE): $robo" }
foreach ($folder in "App_Data", "logs", "wwwroot\uploads") { New-Item -ItemType Directory -Force -Path (Join-Path $SitePath $folder) | Out-Null }

# Production settings: PublicUrl (links inside reminder emails)
$prodSettings = Join-Path $SitePath "appsettings.Production.json"
$json = Get-Content $prodSettings -Raw -Encoding UTF8 | ConvertFrom-Json
if ($PublicUrl) { $json.App.PublicUrl = $PublicUrl }
elseif ($json.App.PublicUrl -like "*CHANGE-ME*") {
    $name = if ($HostName) { $HostName } else { $env:COMPUTERNAME }
    $json.App.PublicUrl = if ($Port -eq 80) { "http://$name" } else { "http://${name}:$Port" }
}
$json.ConnectionStrings.CRMDatabase = "Server=$SqlServer;Database=$Database;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Application Name=CrmSystem;Connect Timeout=30"
$json | ConvertTo-Json -Depth 10 | Set-Content -Path $prodSettings -Encoding UTF8
Ok "PublicUrl = $($json.App.PublicUrl)"

# ---------- 4. Application pool ----------
Step "Application pool '$SiteName'"
$poolPath = "IIS:\AppPools\$SiteName"
if (-not (Test-Path $poolPath)) { New-WebAppPool -Name $SiteName | Out-Null }
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ""
Set-ItemProperty $poolPath -Name startMode -Value "AlwaysRunning"
Set-ItemProperty $poolPath -Name processModel.identityType -Value "ApplicationPoolIdentity"
Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true   # Data Protection / temp folders
Set-ItemProperty $poolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Clear-ItemProperty $poolPath -Name recycling.periodicRestart.schedule
Add-ItemProperty $poolPath -Name recycling.periodicRestart.schedule -Value @{ value = "03:00:00" }
if ($AdminPassword) {
    # Only used once, when the database has no admin user yet (fresh server)
    $envVars = "system.applicationHost/applicationPools/add[@name='$SiteName']/environmentVariables"
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $envVars -Name "." -AtElement @{ name = "SeedAdmin__Password" } -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $envVars -Name "." -Value @{ name = "SeedAdmin__Password"; value = $AdminPassword }
    Ok "SeedAdmin__Password set on the app pool (remove it after the first login)"
}
Ok "No Managed Code, AlwaysRunning, idle timeout 0, daily recycle 03:00"

# ---------- 5. Site ----------
Step "IIS site '$SiteName'"
if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
    if ($Port -eq 80 -and -not $HostName) {
        $default = Get-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
        if ($default -and $default.State -eq "Started") { Stop-Website -Name "Default Web Site"; Warn "'Default Web Site' stopped (it also uses port 80)." }
    }
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null
}
else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true
Ok "Binding: http://$(if ($HostName) { $HostName } else { '*' }):$Port"

# ---------- 6. File permissions ----------
Step "File permissions for $appPoolIdentity"
& icacls $SitePath /grant "${appPoolIdentity}:(OI)(CI)RX" /T /Q | Out-Null
foreach ($folder in "App_Data", "logs", "wwwroot\uploads") {
    & icacls (Join-Path $SitePath $folder) /grant "${appPoolIdentity}:(OI)(CI)M" /T /Q | Out-Null
}
Ok "Read on the site, Modify on App_Data, logs, wwwroot\uploads"

# ---------- 7. Database ----------
Step "Database login + recovery model"
$dbExists = & sqlcmd -S $SqlServer -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database'"
if (($dbExists -join "").Trim() -ne "1") {
    Warn "Database $Database not found on $SqlServer. Create it first: Database\RunAll.ps1 (or restore a backup), then run this script again."
    exit 1
}
# sqlcmd -v cannot pass values containing spaces reliably: substitute into a temp copy
$sqlTemplate = Get-Content (Join-Path $root "Database\07_ProductionSetup.sql") -Raw -Encoding UTF8
$sqlFile = Join-Path $env:TEMP "crm_07_ProductionSetup.sql"
$sqlTemplate.Replace('$(AppLogin)', $appPoolIdentity).Replace('CRMDatabase', $Database) | Set-Content -Path $sqlFile -Encoding UTF8
& sqlcmd -S $SqlServer -E -C -I -b -i $sqlFile
if ($LASTEXITCODE -ne 0) { throw "07_ProductionSetup.sql failed." }
Remove-Item $sqlFile -Force
Ok "Login $appPoolIdentity : db_datareader + db_datawriter"

# ---------- 8. Firewall, backups, start ----------
Step "Firewall"
$ruleName = "CRM System (HTTP $Port)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
}
Ok "Inbound TCP $Port allowed (Domain, Private)"

if ($SetupBackups) {
    Step "Scheduled backups -> $BackupFolder"
    $script = Join-Path $root "Deploy\Backup-CrmDatabase.ps1"
    $common = "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Server `"$SqlServer`" -Database `"$Database`" -BackupFolder `"$BackupFolder`""
    $system = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    $full = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "$common -Type Full"
    $log = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "$common -Type Log"
    $daily = New-ScheduledTaskTrigger -Daily -At "01:00"
    $every30 = New-ScheduledTaskTrigger -Once -At (Get-Date).Date.AddMinutes(30) -RepetitionInterval (New-TimeSpan -Minutes 30)
    Register-ScheduledTask -TaskName "CRM - Full backup" -Action $full -Trigger $daily -Principal $system -Force | Out-Null
    Register-ScheduledTask -TaskName "CRM - Log backup" -Action $log -Trigger $every30 -Principal $system -Force | Out-Null
    # SYSTEM must be able to back up: NT AUTHORITY\SYSTEM is sysadmin on default installs; grant explicitly otherwise
    & sqlcmd -S $SqlServer -E -C -I -b -Q "IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'NT AUTHORITY\SYSTEM') CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS; USE [$Database]; IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'NT AUTHORITY\SYSTEM') CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM]; ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];" | Out-Null
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Server $SqlServer -Database $Database -BackupFolder $BackupFolder -Type Full
    Ok "Daily full backup 01:00 + log backup every 30 minutes"
}

Step "Starting"
Remove-Item (Join-Path $SitePath "app_offline.htm") -ErrorAction SilentlyContinue
if ((Get-WebAppPoolState -Name $SiteName).Value -ne "Started") { Start-WebAppPool -Name $SiteName }
else { Restart-WebAppPool -Name $SiteName }
if ((Get-Website -Name $SiteName).State -ne "Started") { Start-Website -Name $SiteName }

$testHost = if ($HostName) { $HostName } else { "localhost" }
$healthUrl = "http://${testHost}:$Port/health"
$healthy = $false
for ($i = 1; $i -le 20 -and -not $healthy; $i++) {
    try {
        $response = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 15
        if ($response.database -eq "ok") { $healthy = $true; Ok "Health: status=$($response.status) database=$($response.database) reminderEngine=$($response.reminderEngine)" }
    }
    catch { Start-Sleep -Seconds 3 }
}
if (-not $healthy) {
    Warn "The site did not answer $healthUrl. Check: Event Viewer > Windows Logs > Application (source 'IIS AspNetCore Module V2' / 'CrmSystem'),"
    Warn "or set stdoutLogEnabled=`"true`" in $SitePath\web.config and look in $SitePath\logs."
    exit 1
}

Write-Host ""
Write-Host "CRM System is running: $($json.App.PublicUrl)" -ForegroundColor Green
Write-Host "Next: Settings > Email (SMTP) inside the system, and HTTPS certificate if users connect over the internet (see docs\Deployment_IIS.md)."
