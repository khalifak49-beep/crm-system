# CRM System – Deployment on IIS (Windows Server)

دليل نشر النظام على خادم IIS داخل الشركة.
This guide covers deploying the CRM on an IIS server inside the company.

---

## 1. Server requirements

| Item | Requirement |
|---|---|
| Operating system | Windows Server 2019/2022/2025, or Windows 10/11 **Pro/Enterprise** (Windows **Home** has no IIS) |
| .NET | **.NET 10 Hosting Bundle**, from https://dotnet.microsoft.com/download/dotnet/10.0. Install it **after** IIS, or run its *Repair* if IIS was added later. |
| SQL Server | 2019+ (Express works; backups run from Task Scheduler, so SQL Agent is not needed) |
| Tools | `sqlcmd`, which is installed with SQL Server / SSMS |
| Disk | Space for the backups (default `C:\CRM-Backups`, 14 days retention) |

---

## 2. Quick install (one script)

1. Copy the whole project folder to the server, for example `D:\CrmSource`.
2. Create the database on the server if it does not exist yet:
   ```powershell
   cd D:\CrmSource\Database
   .\RunAll.ps1          # creates CRMDatabase + tables + seed data + runs the verification tests
   ```
   You can restore an existing backup instead (see section 6).
3. Open **PowerShell as Administrator** and run:
   ```powershell
   cd D:\CrmSource
   powershell -ExecutionPolicy Bypass -File .\Deploy\Install-CrmOnIIS.ps1 `
       -HostName crm.company.local `
       -SetupBackups `
       -AdminPassword "<a strong password, only for a NEW database>"
   ```

### What the script does

All steps are safe to repeat.

- **IIS:** enables the IIS features (WebSockets and Application Initialization included).
- **.NET:** checks the .NET 10 Hosting Bundle.
- **Publish and copy:** publishes the Release build and copies it to `C:\inetpub\CrmSystem`. It keeps `App_Data`, `logs` and `wwwroot\uploads` (logo) from the previous deployment.
- **Application pool `CrmSystem`:**
  - *No Managed Code*, *AlwaysRunning*, idle timeout 0, with a daily recycle at 03:00.
  - This keeps the **reminder engine** running 24/7.
- **Site:** creates the site with preload enabled.
- **File permissions:** the pool account gets Read on the site and Modify only on `App_Data`, `logs` and `wwwroot\uploads`.
- **Database script** (`Database\07_ProductionSetup.sql`):
  - creates the SQL login `IIS APPPOOL\CrmSystem` with **db_datareader + db_datawriter only** (no schema rights);
  - sets FULL recovery.
- **Firewall:** opens the port.
- **Backups:** with `-SetupBackups`, schedules a daily full backup at 01:00 and a log backup every 30 minutes, then takes the first full backup.
- **Check:** calls `/health` to confirm the site answers.

### Script parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-SiteName` | `CrmSystem` | IIS site + app pool name |
| `-HostName` | *(empty = all names)* | DNS name in the binding, e.g. `crm.company.local` |
| `-Port` | `80` | HTTP port |
| `-SitePath` | `C:\inetpub\CrmSystem` | Physical folder |
| `-SqlServer` | `.` | SQL Server instance, e.g. `.\SQLEXPRESS` or `SQL01` |
| `-Database` | `CRMDatabase` | Database name |
| `-PublicUrl` | `http://<HostName>` | Address used in the links inside reminder e-mails |
| `-AdminPassword` | *(empty)* | Creates the first `admin` user **only if the database has no admin**. Stored as the app-pool environment variable `SeedAdmin__Password`. |
| `-SetupBackups` | off | Registers the backup tasks |
| `-BackupFolder` | `C:\CRM-Backups` | Backup destination. Prefer another disk or a network share. |
| `-SkipPublish` | off | Use the existing `publish\CrmSystem` output |

> After the first login, change the admin password (top-right menu → *Change password*). Then remove the variable: IIS Manager → Configuration Editor → `system.applicationHost/applicationPools` → CrmSystem → environmentVariables.

---

## 3. Updating to a new version

```powershell
cd D:\CrmSource            # the folder with the new source code
powershell -ExecutionPolicy Bypass -File .\Deploy\Install-CrmOnIIS.ps1 -HostName crm.company.local
```

The script places `app_offline.htm` while it copies, so users see an "updating" page. Then it restarts the pool.

These are never overwritten:
- `appsettings.Production.json`
- the uploaded logo
- the import files
- the database

If a new version comes with a new `Database\0X_*.sql` script, run it before the update:
```powershell
sqlcmd -S . -E -C -I -b -i Database\0X_Name.sql
```

---

## 4. Configuration (`C:\inetpub\CrmSystem\appsettings.Production.json`)

| Key | Meaning |
|---|---|
| `ConnectionStrings:CRMDatabase` | Windows authentication as the app pool. With SQL on **another server**: use the IIS machine account `DOMAIN\IISSERVER$`, or run the pool as a domain service account. Run `07_ProductionSetup.sql` with that name as `AppLogin`. |
| `App:PublicUrl` | Address users type, e.g. `https://crm.company.com`. It is used inside reminder e-mails. |
| `App:RequireHttps` | `true` only after a certificate is installed (section 5). It turns on HTTPS redirection, HSTS and secure cookies. |

Everything else is configured **inside the system** (Settings page), stored in the database:
- SMTP: the password is stored encrypted;
- reminder hours;
- time zone (Asia/Muscat);
- password policy;
- session timeout;
- quotation and tender alert days;
- company name and logo.

After you edit the JSON file, recycle the app pool: `Restart-WebAppPool CrmSystem`.

---

## 5. HTTPS (recommended, required if used over the internet)

1. Get a certificate:
   - your company CA;
   - a public certificate;
   - or *win-acme* for Let's Encrypt.
2. In IIS Manager → site **CrmSystem** → *Bindings* → *Add*: type `https`, port 443, host name, select the certificate.
3. Open port 443:
   ```powershell
   New-NetFirewallRule -DisplayName "CRM System (HTTPS 443)" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
   ```
4. In `appsettings.Production.json` set `"RequireHttps": true` and `"PublicUrl": "https://crm.company.com"`, then recycle the pool.

---

## 6. Backups & restore

### Backup schedule

Backups use `Deploy\Backup-CrmDatabase.ps1`, run from Task Scheduler as SYSTEM.

| Task | When | Output |
|---|---|---|
| CRM - Full backup | daily 01:00 | `CRMDatabase_FULL_yyyyMMdd_HHmmss.bak` |
| CRM - Log backup | every 30 min | `CRMDatabase_LOG_yyyyMMdd_HHmmss.trn` |

- Each full backup is checked with `RESTORE VERIFYONLY` and followed by `sp_updatestats`.
- Compression is used when the edition supports it (not on Express).
- Files older than 14 days are deleted, but the newest full backup is always kept.
- The results are written to `C:\CRM-Backups\backup-log.txt`.

**Copy the backup folder to another machine or a network share every day.** A backup on the same disk does not protect against disk failure.

To take a backup manually:
```powershell
powershell -ExecutionPolicy Bypass -File .\Deploy\Backup-CrmDatabase.ps1 -Type Full
```

### Restore

The application must be stopped first.

```powershell
Stop-WebAppPool CrmSystem
```
```sql
-- 1) newest FULL backup
RESTORE DATABASE CRMDatabase FROM DISK = N'C:\CRM-Backups\CRMDatabase_FULL_20261111_010000.bak'
    WITH NORECOVERY, REPLACE;
-- 2) every LOG backup taken AFTER that full backup, oldest first
RESTORE LOG CRMDatabase FROM DISK = N'C:\CRM-Backups\CRMDatabase_LOG_20261111_013000.trn' WITH NORECOVERY;
RESTORE LOG CRMDatabase FROM DISK = N'C:\CRM-Backups\CRMDatabase_LOG_20261111_020000.trn' WITH NORECOVERY;
-- 3) open the database
RESTORE DATABASE CRMDatabase WITH RECOVERY;
```
```powershell
Start-WebAppPool CrmSystem
```

Point-in-time restore: add `STOPAT = '2026-11-11T09:45:00'` to the last `RESTORE LOG`.

**Moving to a new server:**
1. Restore the database.
2. Run `Database\07_ProductionSetup.sql` again, so the login is mapped on the new server.
3. Run `Install-CrmOnIIS.ps1`.

---

## 7. Monitoring & troubleshooting

### Health check

`GET http://<server>/health` needs no login. It returns:
```json
{"status":"ok","database":"ok","reminderEngine":"ok","lastReminderScanUtc":"2026-11-11T09:57:47Z"}
```

| Field | Value | Meaning |
|---|---|---|
| `database` | `unreachable` | HTTP 503: SQL Server is down, or the login was not created |
| `reminderEngine` | `stale` | The reminder engine has not completed a scan for 15 minutes. Look in the Event Log. |
| `reminderEngine` | `starting` | Normal for the first seconds after a restart |

### Common problems

| Symptom | Fix |
|---|---|
| **HTTP 500.19** | The Hosting Bundle is missing, or it was installed before IIS. Install it, or run its *Repair*. |
| **HTTP 500.30 / 500.31** | Startup failed. See *Event Viewer → Windows Logs → Application* (sources `IIS AspNetCore Module V2` and `CrmSystem`). For more detail, set `stdoutLogEnabled="true"` in `web.config`, recycle, and read `logs\stdout_*.log`. **Turn it off again.** |
| `Login failed for user 'IIS APPPOOL\CrmSystem'` | Run `Database\07_ProductionSetup.sql` (the install script does this). |
| Reminders stop at night | The app pool must be *AlwaysRunning* with idle timeout 0, and the site must have *preloadEnabled*. The script sets these. Also check that the **Application Initialization** feature is installed. |
| E-mails contain wrong links | Fix `App:PublicUrl`. |
| Logo upload fails | The pool account needs Modify on `wwwroot\uploads` (the script grants it). |
| Users are logged out after a restart | This should not happen: the Data Protection keys are stored in the database (`DataProtectionKeys`). Check that the table exists. |

### Audit and logs

- Database table `AuditLogs`: every create, update and delete, with the old and new values as JSON, the user and the time. Secrets are masked.
  ```sql
  SELECT TOP 100 * FROM AuditLogs ORDER BY AuditLogId DESC;
  ```
- Database table `NotificationLogs`: one row per channel (in-app, e-mail, browser) for every reminder, with its status and error.
- Windows Event Log: application warnings and errors.

---

## 8. Security checklist

- [ ] Admin password changed after the first login. `SeedAdmin__Password` removed from the app pool.
- [ ] HTTPS binding and `RequireHttps: true`, if the system is reachable outside the LAN.
- [ ] SQL login for the app has only `db_datareader` and `db_datawriter`.
- [ ] Backups copied off the server daily, and a test restore done once.
- [ ] Windows Update and SQL Server cumulative updates applied.

The application already sends security headers (CSP, X-Frame-Options DENY, nosniff, Referrer-Policy, Permissions-Policy) and hides the server name. It also locks an account after failed logins, using the policy on the Settings page.
