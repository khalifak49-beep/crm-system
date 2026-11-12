/* =====================================================================
   CRM System - Phase 1
   File   : Database/01_CreateDatabase.sql
   Purpose: Create the CRMDatabase database (safe: never drops anything).
   Run    : sqlcmd -S . -E -C -I -b -i 01_CreateDatabase.sql
            or open in SSMS and Execute.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE master;
GO

IF DB_ID(N'CRMDatabase') IS NULL
BEGIN
    /* Arabic_100_CI_AS:
       - Correct sorting/comparison for Arabic company and contact names.
       - Case-insensitive for English (OQ BI = oq bi).
       - All text columns are NVARCHAR so Arabic + English are stored safely. */
    CREATE DATABASE CRMDatabase COLLATE Arabic_100_CI_AS;
    PRINT N'Database CRMDatabase created.';
END
ELSE
BEGIN
    PRINT N'Database CRMDatabase already exists - skipped creation.';
END
GO

/* Read Committed Snapshot: readers (dashboard / reports) never block writers
   (users + the reminder BackgroundService). Recommended for web apps. */
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'CRMDatabase' AND is_read_committed_snapshot_on = 0)
BEGIN
    ALTER DATABASE CRMDatabase SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
    PRINT N'READ_COMMITTED_SNAPSHOT enabled.';
END
GO

ALTER DATABASE CRMDatabase SET AUTO_UPDATE_STATISTICS_ASYNC ON;
ALTER DATABASE CRMDatabase SET PAGE_VERIFY CHECKSUM;
GO

/* Development uses SIMPLE recovery. For production switch to FULL
   and configure log backups (covered in Phase 8). */
ALTER DATABASE CRMDatabase SET RECOVERY SIMPLE;
GO

PRINT N'01_CreateDatabase.sql completed.';
GO
