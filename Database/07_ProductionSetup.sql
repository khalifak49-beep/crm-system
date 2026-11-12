/* =====================================================================
   CRM System - Production database setup
   File   : Database/07_ProductionSetup.sql
   Run    : sqlcmd -S . -E -C -I -b -v AppLogin="IIS APPPOOL\CrmSystem" -i 07_ProductionSetup.sql
            (Deploy/Install-CrmOnIIS.ps1 runs it automatically.)

   1. Login + database user for the IIS application pool identity, with LEAST privilege:
        db_datareader + db_datawriter only (no DDL, no ALTER, no db_owner).
      - Same machine as IIS : AppLogin = "IIS APPPOOL\CrmSystem"
      - SQL on another server: AppLogin = "DOMAIN\ServerName$" (the IIS machine account)
        or a domain service account used as the app pool identity.
   2. FULL recovery model so log backups allow point-in-time restore
      (Deploy/Backup-CrmDatabase.ps1 takes the full + log backups).
   3. Production database options.
   Safe to run again.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE master;
GO

/* ---------- 1. Login for the web application ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppLogin)')
BEGIN
    CREATE LOGIN [$(AppLogin)] FROM WINDOWS WITH DEFAULT_DATABASE = [CRMDatabase];
    PRINT N'Login created: $(AppLogin)';
END
ELSE PRINT N'Login already exists: $(AppLogin)';
GO

USE CRMDatabase;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppLogin)')
BEGIN
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)] WITH DEFAULT_SCHEMA = dbo;
    PRINT N'Database user created: $(AppLogin)';
END
GO

ALTER ROLE db_datareader ADD MEMBER [$(AppLogin)];
ALTER ROLE db_datawriter ADD MEMBER [$(AppLogin)];
GO

/* The app never changes the schema: make sure the account has no elevated role. */
IF IS_ROLEMEMBER('db_owner', N'$(AppLogin)') = 1 ALTER ROLE db_owner DROP MEMBER [$(AppLogin)];
IF IS_ROLEMEMBER('db_ddladmin', N'$(AppLogin)') = 1 ALTER ROLE db_ddladmin DROP MEMBER [$(AppLogin)];
GO

/* ---------- 2. Recovery model & options ---------- */
USE master;
GO
ALTER DATABASE CRMDatabase SET RECOVERY FULL;
ALTER DATABASE CRMDatabase SET PAGE_VERIFY CHECKSUM;
ALTER DATABASE CRMDatabase SET AUTO_UPDATE_STATISTICS ON;
ALTER DATABASE CRMDatabase SET AUTO_UPDATE_STATISTICS_ASYNC ON;
ALTER DATABASE CRMDatabase SET AUTO_SHRINK OFF;
GO

/* ---------- 3. Report ---------- */
USE CRMDatabase;
GO
SELECT
    DB_NAME()                                                     AS DatabaseName,
    (SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()) AS RecoveryModel,
    N'$(AppLogin)'                                                AS AppLogin,
    IS_ROLEMEMBER('db_datareader', N'$(AppLogin)')                AS CanRead,
    IS_ROLEMEMBER('db_datawriter', N'$(AppLogin)')                AS CanWrite,
    IS_ROLEMEMBER('db_owner', N'$(AppLogin)')                     AS IsOwner;
PRINT N'07_ProductionSetup.sql completed. IMPORTANT: run a FULL backup now (Deploy/Backup-CrmDatabase.ps1 -Type Full).';
GO
