/* =====================================================================
   CRM System - Phase 1
   File   : Database/06_TestRelationships.sql
   Purpose: Automated tests of relationships, constraints, duplicate
            policy, reminder logic and views.
            Everything runs inside a transaction and is ROLLED BACK,
            so the database stays clean (no test data remains).
   Result : a table of PASS / FAIL. Script raises an error if any fail.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT OFF;   -- expected constraint errors must not abort the test run
GO

USE CRMDatabase;
GO

DECLARE @R TABLE (TestNo INT IDENTITY(1,1), TestName NVARCHAR(200), Passed BIT, Detail NVARCHAR(1000));

DECLARE @UserA NVARCHAR(450) = N'test-user-a-' + CONVERT(NVARCHAR(36), NEWID());
DECLARE @UserB NVARCHAR(450) = N'test-user-b-' + CONVERT(NVARCHAR(36), NEWID());
DECLARE @Client1 INT, @Client2 INT, @Client3 INT, @Contact1 INT, @Contact2 INT, @Contact3 INT;
DECLARE @Act1 INT, @Act2 INT, @Act3 INT, @Fu1 INT, @Fu2 INT, @Fu3 INT, @Proj1 INT, @Quot1 INT, @Tender1 INT;
DECLARE @Notif1 BIGINT, @Code NVARCHAR(20), @Cnt INT, @Dt DATETIME2(0), @Now DATETIME2(0);

BEGIN TRANSACTION;

/* ---------- setup users ---------- */
INSERT dbo.ApplicationUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, FullName, SecurityStamp, ConcurrencyStamp)
VALUES (@UserA, N'emp.a', N'EMP.A', N'emp.a@test.local', N'EMP.A@TEST.LOCAL', N'Employee A', NEWID(), NEWID()),
       (@UserB, N'emp.b', N'EMP.B', N'emp.b@test.local', N'EMP.B@TEST.LOCAL', N'Employee B', NEWID(), NEWID());

INSERT dbo.UserRoles (UserId, RoleId) VALUES (@UserA, N'a1b2c3d4-0003-4000-8000-000000000003');
INSERT @R SELECT N'User linked to SalesEmployee role', CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END, NULL
FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = @UserA AND r.Name = N'SalesEmployee';

/* ---------- T: client created with automatic code ---------- */
INSERT dbo.Clients (CompanyName, Industry, City, AccountManagerId, CreatedBy)
VALUES (N'OQ BI', N'Oil & Gas', N'Muscat', @UserA, @UserA);
SET @Client1 = SCOPE_IDENTITY();
SELECT @Code = CompanyCode FROM dbo.Clients WHERE ClientId = @Client1;
INSERT @R VALUES (N'Client code auto-generated (CL-xxxxx)', CASE WHEN @Code LIKE N'CL-[0-9][0-9][0-9][0-9][0-9]%' THEN 1 ELSE 0 END, @Code);

INSERT dbo.Clients (CompanyName, CompanyNameAr, CreatedBy) VALUES (N'PDO', N'شركة تنمية نفط عمان', @UserA);
SET @Client2 = SCOPE_IDENTITY();

/* ---------- T: duplicate company name (different case / spaces) blocked ---------- */
BEGIN TRY
    INSERT dbo.Clients (CompanyName) VALUES (N'  oq bi ');
    INSERT @R VALUES (N'Duplicate company name blocked (case/space-insensitive)', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Duplicate company name blocked (case/space-insensitive)', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: duplicate company code blocked ---------- */
BEGIN TRY
    INSERT dbo.Clients (CompanyName, CompanyCode) VALUES (N'Another Company', @Code);
    INSERT @R VALUES (N'Duplicate company code blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Duplicate company code blocked', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: Arabic name stored and returned correctly ---------- */
INSERT @R SELECT N'Arabic text stored correctly (NVARCHAR)', CASE WHEN CompanyNameAr = N'شركة تنمية نفط عمان' THEN 1 ELSE 0 END, CompanyNameAr
FROM dbo.Clients WHERE ClientId = @Client2;

/* ---------- T: soft-deleted client frees its name ---------- */
INSERT dbo.Clients (CompanyName) VALUES (N'Old Company LLC');
SET @Client3 = SCOPE_IDENTITY();
UPDATE dbo.Clients SET IsDeleted = 1, DeletedAt = SYSUTCDATETIME(), DeletedBy = @UserA WHERE ClientId = @Client3;
BEGIN TRY
    INSERT dbo.Clients (CompanyName) VALUES (N'Old Company LLC');
    INSERT @R VALUES (N'Soft-deleted client name can be reused', 1, NULL);
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Soft-deleted client name can be reused', 0, ERROR_MESSAGE());
END CATCH;

/* ---------- T: soft delete requires DeletedAt ---------- */
BEGIN TRY
    UPDATE dbo.Clients SET IsDeleted = 1 WHERE ClientId = @Client2;
    INSERT @R VALUES (N'Soft delete without DeletedAt blocked', 0, N'Update succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Soft delete without DeletedAt blocked', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: several contacts per company, only one primary ---------- */
INSERT dbo.Contacts (ClientId, FullName, JobTitle, Department, IsPrimary, CreatedBy) VALUES (@Client2, N'Procurement Officer', N'Procurement Officer', N'Procurement', 1, @UserA);
SET @Contact1 = SCOPE_IDENTITY();
INSERT dbo.Contacts (ClientId, FullName, JobTitle, Department, IsPrimary, CreatedBy) VALUES (@Client2, N'Projects Manager', N'Project Manager', N'Projects', 0, @UserA);
SET @Contact2 = SCOPE_IDENTITY();
INSERT dbo.Contacts (ClientId, FullName, JobTitle, Department, IsPrimary, CreatedBy) VALUES (@Client2, N'Maintenance Officer', N'Maintenance Officer', N'Maintenance', 0, @UserA);
INSERT @R SELECT N'Company has 3 contacts (one-to-many)', CASE WHEN COUNT(*) = 3 THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' contacts')
FROM dbo.Contacts WHERE ClientId = @Client2;

BEGIN TRY
    UPDATE dbo.Contacts SET IsPrimary = 1 WHERE ContactId = @Contact2;
    INSERT @R VALUES (N'Second primary contact blocked', 0, N'Update succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Second primary contact blocked', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

INSERT dbo.Contacts (ClientId, FullName, IsPrimary) VALUES (@Client1, N'Ahmed', 1);
SET @Contact3 = SCOPE_IDENTITY();

/* ---------- T: FK - contact for a non-existing client ---------- */
BEGIN TRY
    INSERT dbo.Contacts (ClientId, FullName) VALUES (-999, N'Ghost');
    INSERT @R VALUES (N'FK: contact with invalid ClientId blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'FK: contact with invalid ClientId blocked', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: several activities same client same day = separate rows ---------- */
INSERT dbo.Activities (ClientId, ContactId, ActivityTypeId, ActivityDate, Notes, Outcome, PerformedById, CreatedBy)
VALUES (@Client1, @Contact3, 1, '2026-11-10',
        N'Contacted the company regarding receiving motors. They will send an email within two days.',
        N'Waiting for Email', @UserA, @UserA);
SET @Act1 = SCOPE_IDENTITY();
INSERT dbo.Activities (ClientId, ActivityTypeId, ActivityDate, Notes, PerformedById, CreatedBy)
VALUES (@Client1, 2, '2026-11-10', N'Sent follow-up email.', @UserA, @UserA);
SET @Act2 = SCOPE_IDENTITY();
INSERT dbo.Activities (ClientId, ActivityTypeId, ActivityDate, Notes, PerformedById, CreatedBy)
VALUES (@Client1, 3, '2026-11-12', N'Facility visit.', @UserB, @UserA);
SET @Act3 = SCOPE_IDENTITY();
INSERT @R SELECT N'Multiple activities same client & same day kept separate', CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' rows on 2026-11-10')
FROM dbo.Activities WHERE ClientId = @Client1 AND ActivityDate = '2026-11-10';

/* ---------- T: long notes are not truncated ---------- */
DECLARE @LongNote NVARCHAR(MAX) = REPLICATE(CAST(N'ملاحظة طويلة - long note. ' AS NVARCHAR(MAX)), 500);
UPDATE dbo.Activities SET Notes = @LongNote WHERE ActivityId = @Act2;
INSERT @R SELECT N'Long notes stored without truncation', CASE WHEN LEN(Notes) = LEN(@LongNote) THEN 1 ELSE 0 END, CONCAT(LEN(Notes), N' chars')
FROM dbo.Activities WHERE ActivityId = @Act2;

/* ---------- T: FK - activity with invalid type ---------- */
BEGIN TRY
    INSERT dbo.Activities (ClientId, ActivityTypeId, ActivityDate, PerformedById) VALUES (@Client1, 999, '2026-11-10', @UserA);
    INSERT @R VALUES (N'FK: activity with invalid ActivityTypeId blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'FK: activity with invalid ActivityTypeId blocked', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: hard delete of client with activities is blocked ---------- */
BEGIN TRY
    DELETE dbo.Clients WHERE ClientId = @Client1;
    INSERT @R VALUES (N'Hard delete of client with history blocked', 0, N'Delete succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Hard delete of client with history blocked', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: follow-up 15/11/2026 10:00 Muscat => reminder 14/11/2026 10:00 Muscat ---------- */
-- 10:00 Asia/Muscat (UTC+4) = 06:00 UTC
INSERT dbo.FollowUps (ClientId, ContactId, FollowUpTypeId, SourceActivityId, Description, DueDate, DueTime, DueAtUtc, AssignedToId, Priority, CreatedBy)
VALUES (@Client1, @Contact3, 1, @Act1, N'Contact the company to check the motor delivery status.',
        '2026-11-15', '10:00', '2026-11-15 06:00', @UserA, 3, @UserA);
SET @Fu1 = SCOPE_IDENTITY();
SELECT @Dt = ReminderAtUtc FROM dbo.FollowUps WHERE FollowUpId = @Fu1;
INSERT @R VALUES (N'ReminderAtUtc = DueAtUtc - 24h (14/11 06:00 UTC = 10:00 Muscat)',
                  CASE WHEN @Dt = '2026-11-14 06:00' THEN 1 ELSE 0 END, CONVERT(NVARCHAR(30), @Dt, 120));

/* ---------- T: reminder engine query picks it exactly at the right time ---------- */
SET @Now = '2026-11-14 05:59:00';
SELECT @Cnt = COUNT(*) FROM dbo.FollowUps WHERE ReminderStatus = 1 AND IsDeleted = 0 AND Status IN (1, 4) AND ReminderAtUtc <= @Now AND FollowUpId = @Fu1;
INSERT @R VALUES (N'Reminder NOT due 1 minute before time', CASE WHEN @Cnt = 0 THEN 1 ELSE 0 END, NULL);
SET @Now = '2026-11-14 06:00:30';
SELECT @Cnt = COUNT(*) FROM dbo.FollowUps WHERE ReminderStatus = 1 AND IsDeleted = 0 AND Status IN (1, 4) AND ReminderAtUtc <= @Now AND FollowUpId = @Fu1;
INSERT @R VALUES (N'Reminder due at 14/11 10:00 Muscat', CASE WHEN @Cnt = 1 THEN 1 ELSE 0 END, NULL);

/* ---------- T: notification created once; duplicate DedupKey impossible ---------- */
INSERT dbo.Notifications (UserId, NotificationType, Title, Message, ClientId, FollowUpId, EventAtLocal, DedupKey)
VALUES (@UserA, 1, N'Reminder: Tomorrow you have to contact OQ BI.',
        N'Reminder: Tomorrow you have to contact OQ BI.' + NCHAR(10) + N'Contact Person: Ahmed' + NCHAR(10) +
        N'Purpose: Check the motor delivery status.' + NCHAR(10) + N'Scheduled Date: 15/11/2026' + NCHAR(10) + N'Scheduled Time: 10:00 AM',
        @Client1, @Fu1, '2026-11-15 10:00', CONCAT('FU-REMIND:', @Fu1, ':202611150600:', @UserA));
SET @Notif1 = SCOPE_IDENTITY();
UPDATE dbo.FollowUps SET ReminderStatus = 2, ReminderSentAtUtc = SYSUTCDATETIME() WHERE FollowUpId = @Fu1;

BEGIN TRY
    INSERT dbo.Notifications (UserId, NotificationType, Title, Message, FollowUpId, DedupKey)
    VALUES (@UserA, 1, N'dup', N'dup', @Fu1, CONCAT('FU-REMIND:', @Fu1, ':202611150600:', @UserA));
    INSERT @R VALUES (N'Duplicate reminder notification blocked (DedupKey)', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Duplicate reminder notification blocked (DedupKey)', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

SELECT @Cnt = COUNT(*) FROM dbo.FollowUps WHERE ReminderStatus = 1 AND IsDeleted = 0 AND Status IN (1, 4) AND ReminderAtUtc <= @Now AND FollowUpId = @Fu1;
INSERT @R VALUES (N'Sent reminder is not picked again by the engine', CASE WHEN @Cnt = 0 THEN 1 ELSE 0 END, NULL);

/* ---------- T: notification logs - one row per channel ---------- */
INSERT dbo.NotificationLogs (NotificationId, Channel, Status, Recipient, AttemptCount, LastAttemptAtUtc, SentAtUtc)
VALUES (@Notif1, 1, 2, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
       (@Notif1, 2, 2, N'emp.a@test.local', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
BEGIN TRY
    INSERT dbo.NotificationLogs (NotificationId, Channel, Status) VALUES (@Notif1, 2, 1);
    INSERT @R VALUES (N'Duplicate email log for same notification blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Duplicate email log for same notification blocked', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: rescheduling moves the reminder automatically ---------- */
UPDATE dbo.FollowUps
SET DueDate = '2026-11-16', DueAtUtc = '2026-11-16 06:00', ReminderStatus = 1, ReminderSentAtUtc = NULL
WHERE FollowUpId = @Fu1;
SELECT @Dt = ReminderAtUtc FROM dbo.FollowUps WHERE FollowUpId = @Fu1;
INSERT @R VALUES (N'Reschedule recalculates ReminderAtUtc', CASE WHEN @Dt = '2026-11-15 06:00' THEN 1 ELSE 0 END, CONVERT(NVARCHAR(30), @Dt, 120));
BEGIN TRY
    INSERT dbo.Notifications (UserId, NotificationType, Title, Message, FollowUpId, DedupKey)
    VALUES (@UserA, 1, N'Reminder (new time)', N'Reminder for rescheduled time', @Fu1, CONCAT('FU-REMIND:', @Fu1, ':202611160600:', @UserA));
    INSERT @R VALUES (N'New reminder allowed for the new schedule', 1, NULL);
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'New reminder allowed for the new schedule', 0, ERROR_MESSAGE());
END CATCH;

/* ---------- T: cancelled / completed follow-ups never reminded ---------- */
INSERT dbo.FollowUps (ClientId, FollowUpTypeId, Description, DueDate, DueTime, DueAtUtc, AssignedToId, Status, CancelledAtUtc, CancelledById, CancelReason)
VALUES (@Client1, 2, N'Cancelled one', '2026-11-11', '10:00', '2026-11-11 06:00', @UserA, 3, SYSUTCDATETIME(), @UserA, N'Not needed');
SET @Fu2 = SCOPE_IDENTITY();
INSERT dbo.FollowUps (ClientId, FollowUpTypeId, Description, DueDate, DueTime, DueAtUtc, AssignedToId, Status, CompletedAtUtc, CompletedById, CompletedActivityId)
VALUES (@Client1, 2, N'Completed one', '2026-11-11', '11:00', '2026-11-11 07:00', @UserA, 2, SYSUTCDATETIME(), @UserA, @Act2);
SET @Fu3 = SCOPE_IDENTITY();
SELECT @Cnt = COUNT(*) FROM dbo.FollowUps
WHERE ReminderStatus = 1 AND IsDeleted = 0 AND Status IN (1, 4) AND ReminderAtUtc <= '2030-01-01' AND FollowUpId IN (@Fu2, @Fu3);
INSERT @R VALUES (N'Cancelled & completed follow-ups excluded from reminders', CASE WHEN @Cnt = 0 THEN 1 ELSE 0 END, NULL);

BEGIN TRY
    INSERT dbo.FollowUps (ClientId, FollowUpTypeId, Description, DueDate, DueAtUtc, AssignedToId, Status)
    VALUES (@Client1, 2, N'Completed without date', '2026-11-11', '2026-11-11 06:00', @UserA, 2);
    INSERT @R VALUES (N'Completed status requires CompletedAtUtc', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Completed status requires CompletedAtUtc', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* ---------- T: overdue detection in view ---------- */
DECLARE @FuOver INT;
INSERT dbo.FollowUps (ClientId, FollowUpTypeId, Description, DueDate, DueTime, DueAtUtc, AssignedToId)
VALUES (@Client2, 1, N'Past due call', '2026-01-01', '09:00', '2026-01-01 05:00', @UserB);
SET @FuOver = SCOPE_IDENTITY();
INSERT @R SELECT N'Pending past-due follow-up shows EffectiveStatus = Overdue', CASE WHEN EffectiveStatus = 4 THEN 1 ELSE 0 END, CONCAT(N'EffectiveStatus=', EffectiveStatus)
FROM dbo.vw_FollowUpDetails WHERE FollowUpId = @FuOver;

/* ---------- T: projects / quotations / tenders relations & checks ---------- */
INSERT dbo.Projects (ClientId, ProjectName, ProjectValue, ExpectedStartDate, ExpectedEndDate, ProjectStatus, Probability, AssignedToId, CreatedBy)
VALUES (@Client1, N'Motor supply 2027', 125000.500, '2027-01-01', '2027-06-30', 2, 40, @UserA, @UserA);
SET @Proj1 = SCOPE_IDENTITY();
INSERT dbo.ProjectNotes (ProjectId, NoteText, CreatedBy) VALUES (@Proj1, N'Client asked for revised specs.', @UserA);
INSERT dbo.Tenders (TenderNumber, ClientId, ProjectId, Title, SubmissionDate, ClosingDate, TenderValue, Status, AssignedToId)
VALUES (N'T-2026-001', @Client1, @Proj1, N'Motors tender', '2026-11-10', '2026-12-01 12:00', 120000, 3, @UserA);
SET @Tender1 = SCOPE_IDENTITY();
INSERT dbo.Quotations (QuotationNumber, ClientId, ContactId, ProjectId, TenderId, SubmissionDate, QuotationValue, ValidUntil, Status, AssignedToId)
VALUES (N'Q-2026-0001', @Client1, @Contact3, @Proj1, @Tender1, '2026-11-10', 118000, '2026-12-10', 2, @UserA);
SET @Quot1 = SCOPE_IDENTITY();
INSERT @R SELECT N'Project -> Tender -> Quotation chain linked to client', CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END, NULL
FROM dbo.Quotations q JOIN dbo.Tenders t ON t.TenderId = q.TenderId JOIN dbo.Projects p ON p.ProjectId = q.ProjectId
WHERE q.QuotationId = @Quot1 AND t.ClientId = @Client1 AND p.ClientId = @Client1;

BEGIN TRY
    INSERT dbo.Projects (ClientId, ProjectName, Probability) VALUES (@Client1, N'Bad probability', 150);
    INSERT @R VALUES (N'Probability > 100 blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Probability > 100 blocked', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    INSERT dbo.Projects (ClientId, ProjectName, ExpectedStartDate, ExpectedEndDate) VALUES (@Client1, N'Bad dates', '2027-05-01', '2027-01-01');
    INSERT @R VALUES (N'Project end date before start blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Project end date before start blocked', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    INSERT dbo.Quotations (QuotationNumber, ClientId) VALUES (N'Q-2026-0001', @Client2);
    INSERT @R VALUES (N'Duplicate quotation number blocked', 0, N'Insert succeeded but should fail');
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Duplicate quotation number blocked', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH;

/* Same tender number for a DIFFERENT client is allowed (tender numbers are per issuer) */
BEGIN TRY
    INSERT dbo.Tenders (TenderNumber, ClientId, Title) VALUES (N'T-2026-001', @Client2, N'PDO tender');
    INSERT @R VALUES (N'Same tender number allowed for another client', 1, NULL);
END TRY
BEGIN CATCH
    INSERT @R VALUES (N'Same tender number allowed for another client', 0, ERROR_MESSAGE());
END CATCH;

/* ---------- T: cascade only where intended (project notes) ---------- */
DECLARE @ProjTmp INT;
INSERT dbo.Projects (ClientId, ProjectName) VALUES (@Client2, N'Temp project');
SET @ProjTmp = SCOPE_IDENTITY();
INSERT dbo.ProjectNotes (ProjectId, NoteText) VALUES (@ProjTmp, N'note');
DELETE dbo.Projects WHERE ProjectId = @ProjTmp;
INSERT @R SELECT N'Deleting project cascades its notes', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END, NULL
FROM dbo.ProjectNotes WHERE ProjectId = @ProjTmp;

/* ---------- T: client assignment (employee access) ---------- */
INSERT dbo.ClientAssignments (ClientId, UserId, AssignedBy) VALUES (@Client2, @UserA, @UserA);
INSERT @R SELECT N'Employee A can access 2 clients (owner + assignment)', CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' clients')
FROM dbo.Clients c
WHERE c.IsDeleted = 0 AND c.ClientId IN (@Client1, @Client2)
  AND (c.AccountManagerId = @UserA OR EXISTS (SELECT 1 FROM dbo.ClientAssignments ca WHERE ca.ClientId = c.ClientId AND ca.UserId = @UserA));

/* ---------- T: views ---------- */
INSERT @R SELECT N'vw_ClientSummary: last contact date = 12/11/2026', CASE WHEN LastActivityDate = '2026-11-12' THEN 1 ELSE 0 END, CONVERT(NVARCHAR(30), LastActivityDate, 120)
FROM dbo.vw_ClientSummary WHERE ClientId = @Client1;
INSERT @R SELECT N'vw_ClientSummary: next follow-up = 16/11/2026', CASE WHEN NextFollowUpDate = '2026-11-16' AND NextFollowUpId = @Fu1 THEN 1 ELSE 0 END, CONVERT(NVARCHAR(30), NextFollowUpDate, 120)
FROM dbo.vw_ClientSummary WHERE ClientId = @Client1;
INSERT @R SELECT N'vw_ClientSummary: primary contact = Ahmed', CASE WHEN PrimaryContactName = N'Ahmed' THEN 1 ELSE 0 END, PrimaryContactName
FROM dbo.vw_ClientSummary WHERE ClientId = @Client1;
INSERT @R SELECT N'vw_ActivityDetails returns 3 activities with names', CASE WHEN COUNT(*) = 3 AND MIN(PerformedByName) IS NOT NULL THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' rows')
FROM dbo.vw_ActivityDetails WHERE ClientId = @Client1;
INSERT @R SELECT N'vw_ClientTimeline combines activities/follow-ups/projects/quotes/tenders', CASE WHEN COUNT(DISTINCT EventKind) = 5 THEN 1 ELSE 0 END, STRING_AGG(EventKind, ',')
FROM (SELECT DISTINCT EventKind FROM dbo.vw_ClientTimeline WHERE ClientId = @Client1) x;

/* Dashboard-style aggregate works from real tables */
INSERT @R SELECT N'Dashboard aggregate: calls/emails/visits counted by type', CASE WHEN SUM(CASE WHEN t.Code = 'COLD_CALL' THEN 1 ELSE 0 END) = 1
    AND SUM(CASE WHEN t.Code = 'EMAIL' THEN 1 ELSE 0 END) = 1 AND SUM(CASE WHEN t.Code = 'VISIT' THEN 1 ELSE 0 END) = 1 THEN 1 ELSE 0 END, NULL
FROM dbo.Activities a JOIN dbo.ActivityTypes t ON t.ActivityTypeId = a.ActivityTypeId
WHERE a.IsDeleted = 0 AND a.ClientId = @Client1;

/* ---------- clean up: undo everything ---------- */
ROLLBACK TRANSACTION;

/* Sequences and IDENTITY values are not transactional in SQL Server.
   If a table is still empty (fresh install), restart its counter so the
   first real company is CL-00001 and the first real Ids start at 1. */
IF NOT EXISTS (SELECT 1 FROM dbo.Clients)
    ALTER SEQUENCE dbo.Seq_ClientCode RESTART WITH 1;

DECLARE @tbl SYSNAME, @sql NVARCHAR(400);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
    FROM sys.tables t
    JOIN sys.identity_columns ic ON ic.object_id = t.object_id
    WHERE ic.last_value IS NOT NULL AND t.name <> N'ActivityTypes';
OPEN cur;
FETCH NEXT FROM cur INTO @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'IF NOT EXISTS (SELECT 1 FROM ' + @tbl + N') DBCC CHECKIDENT (''' + @tbl + N''', RESEED, 0) WITH NO_INFOMSGS;';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM cur INTO @tbl;
END
CLOSE cur;
DEALLOCATE cur;

SELECT TestNo, CASE WHEN Passed = 1 THEN 'PASS' ELSE 'FAIL' END AS Result, TestName,
       LEFT(ISNULL(Detail, N''), 120) AS Detail
FROM @R ORDER BY TestNo;

DECLARE @Failed INT = (SELECT COUNT(*) FROM @R WHERE Passed = 0), @Total INT = (SELECT COUNT(*) FROM @R);
IF @Failed > 0
    RAISERROR(N'%d of %d tests FAILED.', 16, 1, @Failed, @Total);
ELSE
    PRINT CONCAT(N'ALL ', @Total, N' TESTS PASSED. (All test data rolled back.)');
GO
