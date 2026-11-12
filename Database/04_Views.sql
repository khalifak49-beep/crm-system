/* =====================================================================
   CRM System - Phase 1
   File   : Database/04_Views.sql
   Purpose: Read-only views used by grids, client profile, timeline,
            follow-ups screen and reports. (Safe to re-run: CREATE OR ALTER)
   EF Core maps these as keyless entities: .ToView("vw_...").HasNoKey()
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE CRMDatabase;
GO

/* ---------------------------------------------------------------------
   vw_ClientSummary : Clients grid
   last contact date, next follow-up, primary contact, counters.
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_ClientSummary
AS
SELECT
    c.ClientId,
    c.CompanyCode,
    c.CompanyName,
    c.CompanyNameAr,
    c.Industry,
    c.Phone,
    c.Email,
    c.Website,
    c.Country,
    c.City,
    c.Status,
    c.AccountManagerId,
    am.FullName                    AS AccountManagerName,
    pc.ContactId                   AS PrimaryContactId,
    pc.FullName                    AS PrimaryContactName,
    pc.JobTitle                    AS PrimaryContactJobTitle,
    pc.Phone                       AS PrimaryContactPhone,
    pc.Email                       AS PrimaryContactEmail,
    la.ActivityDate                AS LastActivityDate,
    la.ActivityTypeId              AS LastActivityTypeId,
    ISNULL(ac.TotalActivities, 0)  AS TotalActivities,
    nf.FollowUpId                  AS NextFollowUpId,
    nf.DueDate                     AS NextFollowUpDate,
    nf.DueTime                     AS NextFollowUpTime,
    nf.DueAtUtc                    AS NextFollowUpAtUtc,
    ISNULL(ov.OverdueFollowUps, 0) AS OverdueFollowUps,
    ISNULL(pr.OpenProjects, 0)     AS OpenProjects,
    c.CreatedAt,
    c.CreatedBy
FROM dbo.Clients AS c
LEFT JOIN dbo.ApplicationUsers AS am ON am.Id = c.AccountManagerId
OUTER APPLY (
    SELECT TOP (1) x.ContactId, x.FullName, x.JobTitle, x.Phone, x.Email
    FROM dbo.Contacts AS x
    WHERE x.ClientId = c.ClientId AND x.IsDeleted = 0
    ORDER BY x.IsPrimary DESC, x.ContactId
) AS pc
OUTER APPLY (
    SELECT TOP (1) a.ActivityDate, a.ActivityTypeId
    FROM dbo.Activities AS a
    WHERE a.ClientId = c.ClientId AND a.IsDeleted = 0
    ORDER BY a.ActivityDate DESC, a.ActivityTime DESC, a.ActivityId DESC
) AS la
OUTER APPLY (
    SELECT COUNT_BIG(*) AS TotalActivities
    FROM dbo.Activities AS a
    WHERE a.ClientId = c.ClientId AND a.IsDeleted = 0
) AS ac
OUTER APPLY (
    SELECT TOP (1) f.FollowUpId, f.DueDate, f.DueTime, f.DueAtUtc
    FROM dbo.FollowUps AS f
    WHERE f.ClientId = c.ClientId AND f.IsDeleted = 0 AND f.Status IN (1, 4)
    ORDER BY f.DueAtUtc
) AS nf
OUTER APPLY (
    SELECT COUNT_BIG(*) AS OverdueFollowUps
    FROM dbo.FollowUps AS f
    WHERE f.ClientId = c.ClientId AND f.IsDeleted = 0
      AND f.Status IN (1, 4) AND f.DueAtUtc < SYSUTCDATETIME()
) AS ov
OUTER APPLY (
    SELECT COUNT_BIG(*) AS OpenProjects
    FROM dbo.Projects AS p
    WHERE p.ClientId = c.ClientId AND p.IsDeleted = 0 AND p.ProjectStatus NOT IN (6, 7)
) AS pr
WHERE c.IsDeleted = 0;
GO

/* ---------------------------------------------------------------------
   vw_ActivityDetails : Activities grid + reports
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_ActivityDetails
AS
SELECT
    a.ActivityId,
    a.ClientId,
    c.CompanyName,
    c.CompanyNameAr,
    c.CompanyCode,
    a.ContactId,
    ct.FullName      AS ContactName,
    a.ActivityTypeId,
    t.Code           AS ActivityTypeCode,
    t.NameEn         AS ActivityTypeNameEn,
    t.NameAr         AS ActivityTypeNameAr,
    t.IconName       AS ActivityTypeIcon,
    t.ColorHex       AS ActivityTypeColor,
    a.ActivityDate,
    a.ActivityTime,
    YEAR(a.ActivityDate)  AS ActivityYear,
    MONTH(a.ActivityDate) AS ActivityMonth,
    a.Subject,
    a.Notes,
    a.Outcome,
    a.ProjectId,
    p.ProjectName,
    a.QuotationId,
    a.TenderId,
    a.PerformedById,
    pu.FullName      AS PerformedByName,
    a.ImportBatchId,
    a.CreatedAt,
    a.CreatedBy,
    cu.FullName      AS CreatedByName
FROM dbo.Activities AS a
INNER JOIN dbo.Clients          AS c  ON c.ClientId = a.ClientId
INNER JOIN dbo.ActivityTypes    AS t  ON t.ActivityTypeId = a.ActivityTypeId
INNER JOIN dbo.ApplicationUsers AS pu ON pu.Id = a.PerformedById
LEFT  JOIN dbo.Contacts         AS ct ON ct.ContactId = a.ContactId
LEFT  JOIN dbo.Projects         AS p  ON p.ProjectId = a.ProjectId
LEFT  JOIN dbo.ApplicationUsers AS cu ON cu.Id = a.CreatedBy
WHERE a.IsDeleted = 0 AND c.IsDeleted = 0;
GO

/* ---------------------------------------------------------------------
   vw_FollowUpDetails : Follow-ups screen
   EffectiveStatus = 4 (Overdue) as soon as a Pending follow-up passes
   its due time, even before the BackgroundService updates Status.
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_FollowUpDetails
AS
SELECT
    f.FollowUpId,
    f.ClientId,
    c.CompanyName,
    c.CompanyNameAr,
    f.ContactId,
    ct.FullName     AS ContactName,
    ct.Phone        AS ContactPhone,
    ct.Mobile       AS ContactMobile,
    ct.Email        AS ContactEmail,
    f.FollowUpTypeId,
    t.Code          AS FollowUpTypeCode,
    t.NameEn        AS FollowUpTypeNameEn,
    t.NameAr        AS FollowUpTypeNameAr,
    t.IconName      AS FollowUpTypeIcon,
    t.ColorHex      AS FollowUpTypeColor,
    f.Description,
    f.DueDate,
    f.DueTime,
    f.DueAtUtc,
    f.TimeZoneId,
    f.Priority,
    f.Status,
    CAST(CASE WHEN f.Status = 1 AND f.DueAtUtc < SYSUTCDATETIME() THEN 4 ELSE f.Status END AS TINYINT) AS EffectiveStatus,
    f.ReminderMinutesBefore,
    f.ReminderAtUtc,
    f.ReminderStatus,
    f.ReminderSentAtUtc,
    f.AssignedToId,
    au.FullName     AS AssignedToName,
    au.Email        AS AssignedToEmail,
    f.SourceActivityId,
    sa.ActivityDate AS SourceActivityDate,
    sat.NameEn      AS SourceActivityTypeNameEn,
    sat.NameAr      AS SourceActivityTypeNameAr,
    sa.Notes        AS SourceActivityNotes,
    sa.Outcome      AS SourceActivityOutcome,
    f.ParentFollowUpId,
    f.ProjectId,
    p.ProjectName,
    f.QuotationId,
    f.TenderId,
    f.CompletedAtUtc,
    f.CompletedById,
    f.CompletedActivityId,
    f.CompletionNotes,
    f.CancelledAtUtc,
    f.CancelReason,
    f.CreatedAt,
    f.CreatedBy
FROM dbo.FollowUps AS f
INNER JOIN dbo.Clients          AS c   ON c.ClientId = f.ClientId
INNER JOIN dbo.ActivityTypes    AS t   ON t.ActivityTypeId = f.FollowUpTypeId
INNER JOIN dbo.ApplicationUsers AS au  ON au.Id = f.AssignedToId
LEFT  JOIN dbo.Contacts         AS ct  ON ct.ContactId = f.ContactId
LEFT  JOIN dbo.Activities       AS sa  ON sa.ActivityId = f.SourceActivityId
LEFT  JOIN dbo.ActivityTypes    AS sat ON sat.ActivityTypeId = sa.ActivityTypeId
LEFT  JOIN dbo.Projects         AS p   ON p.ProjectId = f.ProjectId
WHERE f.IsDeleted = 0 AND c.IsDeleted = 0;
GO

/* ---------------------------------------------------------------------
   vw_ClientTimeline : full history of a client in ONE list
   EventAtLocal is in Asia/Muscat local time for every event type.
   Usage: SELECT * FROM vw_ClientTimeline WHERE ClientId = @id
          ORDER BY EventAtLocal DESC, SortKey DESC
   Note : 'Arabian Standard Time' is the Windows id of Asia/Muscat (UTC+4).
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_ClientTimeline
AS
/* Activities: calls, emails, visits, meetings ... */
SELECT
    a.ClientId,
    DATEADD(SECOND, DATEDIFF(SECOND, CAST('00:00:00' AS TIME(0)), ISNULL(a.ActivityTime, CAST('00:00:00' AS TIME(0)))),
            CAST(a.ActivityDate AS DATETIME2(0))) AS EventAtLocal,
    CAST('Activity' AS VARCHAR(20))       AS EventKind,
    CAST(t.Code AS VARCHAR(30))           AS EventCode,
    CAST(ISNULL(a.Subject, t.NameEn) AS NVARCHAR(250)) AS TitleEn,
    CAST(ISNULL(a.Subject, t.NameAr) AS NVARCHAR(250)) AS TitleAr,
    a.Notes                               AS Details,
    a.Outcome                             AS Outcome,
    t.IconName,
    t.ColorHex,
    a.ActivityId                          AS ReferenceId,
    a.PerformedById                       AS UserId,
    CAST(a.ActivityId AS BIGINT) * 10 + 1 AS SortKey
FROM dbo.Activities AS a
INNER JOIN dbo.ActivityTypes AS t ON t.ActivityTypeId = a.ActivityTypeId
WHERE a.IsDeleted = 0

UNION ALL

/* Follow-ups: scheduled (pending / overdue / completed / cancelled) */
SELECT
    f.ClientId,
    DATEADD(SECOND, DATEDIFF(SECOND, CAST('00:00:00' AS TIME(0)), f.DueTime), CAST(f.DueDate AS DATETIME2(0))),
    CAST('FollowUp' AS VARCHAR(20)),
    CAST(CASE WHEN f.Status = 1 AND f.DueAtUtc < SYSUTCDATETIME() THEN 'OVERDUE'
              WHEN f.Status = 1 THEN 'PENDING'
              WHEN f.Status = 2 THEN 'COMPLETED'
              WHEN f.Status = 3 THEN 'CANCELLED'
              ELSE 'OVERDUE' END AS VARCHAR(30)),
    CAST(N'Follow-up Scheduled: ' + t.NameEn AS NVARCHAR(250)),
    CAST(N'متابعة مجدولة: ' + t.NameAr AS NVARCHAR(250)),
    f.Description,
    f.CompletionNotes,
    CAST('calendar' AS VARCHAR(50)),
    CAST('#FF9500' AS CHAR(7)),
    f.FollowUpId,
    f.AssignedToId,
    CAST(f.FollowUpId AS BIGINT) * 10 + 2
FROM dbo.FollowUps AS f
INNER JOIN dbo.ActivityTypes AS t ON t.ActivityTypeId = f.FollowUpTypeId
WHERE f.IsDeleted = 0

UNION ALL

/* Projects created */
SELECT
    p.ClientId,
    CAST(CAST(p.CreatedAt AS DATETIMEOFFSET(3)) AT TIME ZONE 'Arabian Standard Time' AS DATETIME2(0)),
    CAST('Project' AS VARCHAR(20)),
    CAST(CASE p.ProjectStatus WHEN 6 THEN 'WON' WHEN 7 THEN 'LOST' ELSE 'OPEN' END AS VARCHAR(30)),
    CAST(N'Project: ' + p.ProjectName AS NVARCHAR(250)),
    CAST(N'مشروع: ' + p.ProjectName AS NVARCHAR(250)),
    p.ProjectDescription,
    CAST(NULL AS NVARCHAR(500)),
    CAST('briefcase' AS VARCHAR(50)),
    CAST('#5856D6' AS CHAR(7)),
    p.ProjectId,
    p.AssignedToId,
    CAST(p.ProjectId AS BIGINT) * 10 + 3
FROM dbo.Projects AS p
WHERE p.IsDeleted = 0

UNION ALL

/* Quotations sent */
SELECT
    q.ClientId,
    ISNULL(CAST(q.SubmissionDate AS DATETIME2(0)),
           CAST(CAST(q.CreatedAt AS DATETIMEOFFSET(3)) AT TIME ZONE 'Arabian Standard Time' AS DATETIME2(0))),
    CAST('Quotation' AS VARCHAR(20)),
    CAST('QUOTATION' AS VARCHAR(30)),
    CAST(N'Quotation ' + q.QuotationNumber AS NVARCHAR(250)),
    CAST(N'عرض سعر ' + q.QuotationNumber AS NVARCHAR(250)),
    q.Notes,
    CAST(NULL AS NVARCHAR(500)),
    CAST('doc_text' AS VARCHAR(50)),
    CAST('#34C759' AS CHAR(7)),
    q.QuotationId,
    q.AssignedToId,
    CAST(q.QuotationId AS BIGINT) * 10 + 4
FROM dbo.Quotations AS q
WHERE q.IsDeleted = 0

UNION ALL

/* Tenders */
SELECT
    td.ClientId,
    ISNULL(CAST(td.SubmissionDate AS DATETIME2(0)),
           CAST(CAST(td.CreatedAt AS DATETIMEOFFSET(3)) AT TIME ZONE 'Arabian Standard Time' AS DATETIME2(0))),
    CAST('Tender' AS VARCHAR(20)),
    CAST('TENDER' AS VARCHAR(30)),
    CAST(N'Tender ' + td.TenderNumber + N' - ' + td.Title AS NVARCHAR(250)),
    CAST(N'مناقصة ' + td.TenderNumber + N' - ' + td.Title AS NVARCHAR(250)),
    td.Notes,
    CAST(NULL AS NVARCHAR(500)),
    CAST('hammer' AS VARCHAR(50)),
    CAST('#AF52DE' AS CHAR(7)),
    td.TenderId,
    td.AssignedToId,
    CAST(td.TenderId AS BIGINT) * 10 + 5
FROM dbo.Tenders AS td
WHERE td.IsDeleted = 0;
GO

PRINT N'04_Views.sql completed.';
GO
