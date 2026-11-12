/* =====================================================================
   CRM System - Phase 1
   File   : Database/03_CoreTables.sql
   Purpose: All CRM business tables with PK, FK, Unique, Check,
            Default constraints and performance indexes.

   CONVENTIONS (important - the C# code in Phase 2 follows them):
   - *AtUtc / CreatedAt / UpdatedAt  : UTC time   (SYSUTCDATETIME()).
   - Business dates typed by the user (ActivityDate, DueDate, DueTime,
     SubmissionDate...) are LOCAL time of Asia/Muscat (UTC+4).
   - FollowUps.DueAtUtc is calculated by the application from
     DueDate + DueTime + TimeZone, and the reminder time is a
     PERSISTED computed column => it is always correct after any edit.
   - Status columns are TINYINT mapped to C# enums (values documented
     beside each CHECK constraint). Text for EN/AR comes from .resx.
   - Soft delete: IsDeleted / DeletedAt / DeletedBy on important tables.
   - RowVersion: optimistic concurrency (two users editing same record).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

USE CRMDatabase;
GO

IF OBJECT_ID(N'dbo.Clients', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'Core tables already exist. Script 03 skipped (nothing changed).', 10, 1);
    SET NOEXEC ON;
END
GO

/* =====================================================================
   SEQUENCE for automatic company codes: CL-00001, CL-00002, ...
   ===================================================================== */
CREATE SEQUENCE dbo.Seq_ClientCode AS INT START WITH 1 INCREMENT BY 1 NO CYCLE;
GO

/* =====================================================================
   1) ActivityTypes  (lookup - bilingual)
   ===================================================================== */
CREATE TABLE dbo.ActivityTypes
(
    ActivityTypeId   INT IDENTITY(1,1) NOT NULL,
    Code             VARCHAR(30)    NOT NULL,           -- COLD_CALL, EMAIL, VISIT ...
    NameEn           NVARCHAR(100)  NOT NULL,
    NameAr           NVARCHAR(100)  NOT NULL,
    IconName         VARCHAR(50)    NOT NULL CONSTRAINT DF_ActivityTypes_IconName DEFAULT ('circle'),
    ColorHex         CHAR(7)        NOT NULL CONSTRAINT DF_ActivityTypes_ColorHex DEFAULT ('#8E8E93'),
    ExcelColumnName  NVARCHAR(50)   NULL,               -- mapping for the legacy Excel import
    SortOrder        INT            NOT NULL CONSTRAINT DF_ActivityTypes_SortOrder DEFAULT (0),
    IsSystem         BIT            NOT NULL CONSTRAINT DF_ActivityTypes_IsSystem DEFAULT (0),  -- system types cannot be deleted
    IsActive         BIT            NOT NULL CONSTRAINT DF_ActivityTypes_IsActive DEFAULT (1),
    CreatedAt        DATETIME2(3)   NOT NULL CONSTRAINT DF_ActivityTypes_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy        NVARCHAR(450)  NULL,
    UpdatedAt        DATETIME2(3)   NULL,
    UpdatedBy        NVARCHAR(450)  NULL,
    CONSTRAINT PK_ActivityTypes PRIMARY KEY CLUSTERED (ActivityTypeId),
    CONSTRAINT UQ_ActivityTypes_Code UNIQUE (Code),
    CONSTRAINT CK_ActivityTypes_ColorHex CHECK (ColorHex LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),
    CONSTRAINT FK_ActivityTypes_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_ActivityTypes_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.ApplicationUsers (Id)
);
GO

/* =====================================================================
   2) ImportBatches  (history of every Excel import)
   ===================================================================== */
CREATE TABLE dbo.ImportBatches
(
    ImportBatchId      INT IDENTITY(1,1) NOT NULL,
    FileName           NVARCHAR(260)  NOT NULL,
    FileHash           CHAR(64)       NOT NULL,          -- SHA-256: warns if the same file is imported again
    FileSizeBytes      BIGINT         NOT NULL CONSTRAINT DF_ImportBatches_FileSizeBytes DEFAULT (0),
    Status             TINYINT        NOT NULL CONSTRAINT DF_ImportBatches_Status DEFAULT (1),
                       -- 1=Previewed 2=Completed 3=Failed 4=RolledBack
    TotalRows          INT            NOT NULL CONSTRAINT DF_ImportBatches_TotalRows DEFAULT (0),
    ImportedRows       INT            NOT NULL CONSTRAINT DF_ImportBatches_ImportedRows DEFAULT (0),
    SkippedRows        INT            NOT NULL CONSTRAINT DF_ImportBatches_SkippedRows DEFAULT (0),
    DuplicateRows      INT            NOT NULL CONSTRAINT DF_ImportBatches_DuplicateRows DEFAULT (0),
    ErrorRows          INT            NOT NULL CONSTRAINT DF_ImportBatches_ErrorRows DEFAULT (0),
    ClientsCreated     INT            NOT NULL CONSTRAINT DF_ImportBatches_ClientsCreated DEFAULT (0),
    ActivitiesCreated  INT            NOT NULL CONSTRAINT DF_ImportBatches_ActivitiesCreated DEFAULT (0),
    DefaultPerformerId NVARCHAR(450)  NULL,              -- employee credited with imported activities
    ErrorsJson         NVARCHAR(MAX)  NULL,
    ImportedBy         NVARCHAR(450)  NOT NULL,
    StartedAtUtc       DATETIME2(3)   NOT NULL CONSTRAINT DF_ImportBatches_StartedAtUtc DEFAULT (SYSUTCDATETIME()),
    CompletedAtUtc     DATETIME2(3)   NULL,
    CONSTRAINT PK_ImportBatches PRIMARY KEY CLUSTERED (ImportBatchId),
    CONSTRAINT CK_ImportBatches_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_ImportBatches_ErrorsJson CHECK (ErrorsJson IS NULL OR ISJSON(ErrorsJson) = 1),
    CONSTRAINT FK_ImportBatches_ImportedBy FOREIGN KEY (ImportedBy) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_ImportBatches_DefaultPerformer FOREIGN KEY (DefaultPerformerId) REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE NONCLUSTERED INDEX IX_ImportBatches_FileHash ON dbo.ImportBatches (FileHash);
GO

/* =====================================================================
   3) Clients  (companies)
   DUPLICATE POLICY:
   - CompanyCode is unique among non-deleted clients (auto CL-00001).
   - CompanyName is unique among non-deleted clients, compared on
     NormalizedName = UPPER(TRIM(name)) + case-insensitive collation,
     so "OQ BI", "oq bi" and " OQ BI " are the same company.
     (The app also collapses double spaces before saving.)
   - An archived client (Status=4) still blocks duplicates.
     Only a soft-deleted client frees its name/code.
   ===================================================================== */
CREATE TABLE dbo.Clients
(
    ClientId          INT IDENTITY(1,1) NOT NULL,
    CompanyCode       NVARCHAR(20)   NOT NULL
                      CONSTRAINT DF_Clients_CompanyCode DEFAULT (N'CL-' + FORMAT(NEXT VALUE FOR dbo.Seq_ClientCode, 'D5')),
    CompanyName       NVARCHAR(200)  NOT NULL,
    CompanyNameAr     NVARCHAR(200)  NULL,
    NormalizedName    AS CAST(UPPER(LTRIM(RTRIM(CompanyName))) AS NVARCHAR(200)) PERSISTED,
    Industry          NVARCHAR(100)  NULL,
    Phone             NVARCHAR(30)   NULL,
    Email             NVARCHAR(256)  NULL,
    Website           NVARCHAR(256)  NULL,
    Address           NVARCHAR(500)  NULL,
    Country           NVARCHAR(100)  NOT NULL CONSTRAINT DF_Clients_Country DEFAULT (N'Oman'),
    City              NVARCHAR(100)  NULL,
    Status            TINYINT        NOT NULL CONSTRAINT DF_Clients_Status DEFAULT (1),
                      -- 1=Active 2=Inactive 3=Prospect 4=Archived
    Description       NVARCHAR(MAX)  NULL,
    AccountManagerId  NVARCHAR(450)  NULL,              -- employee who owns the account
    Source            TINYINT        NOT NULL CONSTRAINT DF_Clients_Source DEFAULT (1),
                      -- 1=Manual 2=ExcelImport
    ImportBatchId     INT            NULL,
    CreatedAt         DATETIME2(3)   NOT NULL CONSTRAINT DF_Clients_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy         NVARCHAR(450)  NULL,
    UpdatedAt         DATETIME2(3)   NULL,
    UpdatedBy         NVARCHAR(450)  NULL,
    IsDeleted         BIT            NOT NULL CONSTRAINT DF_Clients_IsDeleted DEFAULT (0),
    DeletedAt         DATETIME2(3)   NULL,
    DeletedBy         NVARCHAR(450)  NULL,
    RowVersion        ROWVERSION     NOT NULL,
    CONSTRAINT PK_Clients PRIMARY KEY CLUSTERED (ClientId),
    CONSTRAINT CK_Clients_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_Clients_Source CHECK (Source IN (1, 2)),
    CONSTRAINT CK_Clients_CompanyName CHECK (LEN(LTRIM(RTRIM(CompanyName))) > 0),
    CONSTRAINT CK_Clients_Email CHECK (Email IS NULL OR Email LIKE '%_@_%._%'),
    CONSTRAINT CK_Clients_SoftDelete CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_Clients_AccountManager FOREIGN KEY (AccountManagerId) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Clients_ImportBatch    FOREIGN KEY (ImportBatchId)    REFERENCES dbo.ImportBatches (ImportBatchId),
    CONSTRAINT FK_Clients_CreatedBy      FOREIGN KEY (CreatedBy)        REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Clients_UpdatedBy      FOREIGN KEY (UpdatedBy)        REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Clients_DeletedBy      FOREIGN KEY (DeletedBy)        REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE UNIQUE NONCLUSTERED INDEX UX_Clients_CompanyCode
    ON dbo.Clients (CompanyCode) WHERE IsDeleted = 0;
CREATE UNIQUE NONCLUSTERED INDEX UX_Clients_NormalizedName
    ON dbo.Clients (NormalizedName) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Clients_Status
    ON dbo.Clients (Status) INCLUDE (CompanyName, AccountManagerId) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Clients_AccountManagerId
    ON dbo.Clients (AccountManagerId) INCLUDE (CompanyName, Status) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Clients_ImportBatchId
    ON dbo.Clients (ImportBatchId) WHERE ImportBatchId IS NOT NULL;
GO

/* =====================================================================
   4) ClientAssignments  (Sales Employee <-> Clients he may manage)
   Employee sees a client when: AccountManagerId = me
                             OR a row exists here for me.
   ===================================================================== */
CREATE TABLE dbo.ClientAssignments
(
    ClientId       INT            NOT NULL,
    UserId         NVARCHAR(450)  NOT NULL,
    CanEdit        BIT            NOT NULL CONSTRAINT DF_ClientAssignments_CanEdit DEFAULT (1),
    AssignedAtUtc  DATETIME2(3)   NOT NULL CONSTRAINT DF_ClientAssignments_AssignedAtUtc DEFAULT (SYSUTCDATETIME()),
    AssignedBy     NVARCHAR(450)  NULL,
    CONSTRAINT PK_ClientAssignments PRIMARY KEY CLUSTERED (ClientId, UserId),
    CONSTRAINT FK_ClientAssignments_Clients    FOREIGN KEY (ClientId)   REFERENCES dbo.Clients (ClientId) ON DELETE CASCADE,
    CONSTRAINT FK_ClientAssignments_User       FOREIGN KEY (UserId)     REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_ClientAssignments_AssignedBy FOREIGN KEY (AssignedBy) REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE NONCLUSTERED INDEX IX_ClientAssignments_UserId ON dbo.ClientAssignments (UserId);
GO

/* =====================================================================
   5) Contacts  (many people per company, max ONE primary contact)
   ===================================================================== */
CREATE TABLE dbo.Contacts
(
    ContactId    INT IDENTITY(1,1) NOT NULL,
    ClientId     INT            NOT NULL,
    FullName     NVARCHAR(150)  NOT NULL,
    JobTitle     NVARCHAR(100)  NULL,
    Department   NVARCHAR(100)  NULL,
    Phone        NVARCHAR(30)   NULL,
    Mobile       NVARCHAR(30)   NULL,
    Email        NVARCHAR(256)  NULL,
    Notes        NVARCHAR(MAX)  NULL,
    IsPrimary    BIT            NOT NULL CONSTRAINT DF_Contacts_IsPrimary DEFAULT (0),
    IsActive     BIT            NOT NULL CONSTRAINT DF_Contacts_IsActive DEFAULT (1),
    CreatedAt    DATETIME2(3)   NOT NULL CONSTRAINT DF_Contacts_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy    NVARCHAR(450)  NULL,
    UpdatedAt    DATETIME2(3)   NULL,
    UpdatedBy    NVARCHAR(450)  NULL,
    IsDeleted    BIT            NOT NULL CONSTRAINT DF_Contacts_IsDeleted DEFAULT (0),
    DeletedAt    DATETIME2(3)   NULL,
    DeletedBy    NVARCHAR(450)  NULL,
    RowVersion   ROWVERSION     NOT NULL,
    CONSTRAINT PK_Contacts PRIMARY KEY CLUSTERED (ContactId),
    CONSTRAINT CK_Contacts_FullName CHECK (LEN(LTRIM(RTRIM(FullName))) > 0),
    CONSTRAINT CK_Contacts_Email CHECK (Email IS NULL OR Email LIKE '%_@_%._%'),
    CONSTRAINT CK_Contacts_SoftDelete CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_Contacts_Clients   FOREIGN KEY (ClientId)  REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_Contacts_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Contacts_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Contacts_DeletedBy FOREIGN KEY (DeletedBy) REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE NONCLUSTERED INDEX IX_Contacts_ClientId
    ON dbo.Contacts (ClientId) INCLUDE (FullName, JobTitle, Phone, Email, IsPrimary) WHERE IsDeleted = 0;
CREATE UNIQUE NONCLUSTERED INDEX UX_Contacts_OnePrimaryPerClient
    ON dbo.Contacts (ClientId) WHERE IsPrimary = 1 AND IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Contacts_FullName
    ON dbo.Contacts (FullName) WHERE IsDeleted = 0;
GO

/* =====================================================================
   6) Projects  (business opportunities)
   ===================================================================== */
CREATE TABLE dbo.Projects
(
    ProjectId           INT IDENTITY(1,1) NOT NULL,
    ClientId            INT             NOT NULL,
    ProjectName         NVARCHAR(200)   NOT NULL,
    ProjectDescription  NVARCHAR(MAX)   NULL,
    ProjectValue        DECIMAL(18,3)   NULL,           -- 3 decimals: Omani Rial (baisa)
    Currency            CHAR(3)         NOT NULL CONSTRAINT DF_Projects_Currency DEFAULT ('OMR'),
    ExpectedStartDate   DATE            NULL,
    ExpectedEndDate     DATE            NULL,
    ProjectStatus       TINYINT         NOT NULL CONSTRAINT DF_Projects_ProjectStatus DEFAULT (1),
                        -- 1=New 2=Discussion 3=QuotationSent 4=Tender 5=Negotiation 6=Won 7=Lost 8=OnHold
    Probability         TINYINT         NOT NULL CONSTRAINT DF_Projects_Probability DEFAULT (10),  -- 0..100 %
    AssignedToId        NVARCHAR(450)   NULL,
    ClosedDate          DATE            NULL,           -- date Won / Lost
    LostReason          NVARCHAR(500)   NULL,
    CreatedAt           DATETIME2(3)    NOT NULL CONSTRAINT DF_Projects_CreatedAt DEFAULT (SYSUTCDATETIME()),  -- = CreatedDate
    CreatedBy           NVARCHAR(450)   NULL,
    UpdatedAt           DATETIME2(3)    NULL,
    UpdatedBy           NVARCHAR(450)   NULL,
    IsDeleted           BIT             NOT NULL CONSTRAINT DF_Projects_IsDeleted DEFAULT (0),
    DeletedAt           DATETIME2(3)    NULL,
    DeletedBy           NVARCHAR(450)   NULL,
    RowVersion          ROWVERSION      NOT NULL,
    CONSTRAINT PK_Projects PRIMARY KEY CLUSTERED (ProjectId),
    CONSTRAINT CK_Projects_Status      CHECK (ProjectStatus BETWEEN 1 AND 8),
    CONSTRAINT CK_Projects_Probability CHECK (Probability BETWEEN 0 AND 100),
    CONSTRAINT CK_Projects_Value       CHECK (ProjectValue IS NULL OR ProjectValue >= 0),
    CONSTRAINT CK_Projects_Dates       CHECK (ExpectedEndDate IS NULL OR ExpectedStartDate IS NULL OR ExpectedEndDate >= ExpectedStartDate),
    CONSTRAINT CK_Projects_SoftDelete  CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_Projects_Clients     FOREIGN KEY (ClientId)     REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_Projects_AssignedTo  FOREIGN KEY (AssignedToId) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Projects_CreatedBy   FOREIGN KEY (CreatedBy)    REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Projects_UpdatedBy   FOREIGN KEY (UpdatedBy)    REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Projects_DeletedBy   FOREIGN KEY (DeletedBy)    REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE NONCLUSTERED INDEX IX_Projects_ClientId
    ON dbo.Projects (ClientId) INCLUDE (ProjectStatus, ProjectValue) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Projects_Status
    ON dbo.Projects (ProjectStatus) INCLUDE (ClientId, ProjectValue, AssignedToId) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Projects_AssignedToId
    ON dbo.Projects (AssignedToId, ProjectStatus) WHERE IsDeleted = 0;
GO

/* =====================================================================
   7) ProjectNotes
   ===================================================================== */
CREATE TABLE dbo.ProjectNotes
(
    ProjectNoteId  INT IDENTITY(1,1) NOT NULL,
    ProjectId      INT            NOT NULL,
    NoteText       NVARCHAR(MAX)  NOT NULL,
    CreatedAt      DATETIME2(3)   NOT NULL CONSTRAINT DF_ProjectNotes_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy      NVARCHAR(450)  NULL,
    UpdatedAt      DATETIME2(3)   NULL,
    UpdatedBy      NVARCHAR(450)  NULL,
    IsDeleted      BIT            NOT NULL CONSTRAINT DF_ProjectNotes_IsDeleted DEFAULT (0),
    CONSTRAINT PK_ProjectNotes PRIMARY KEY CLUSTERED (ProjectNoteId),
    CONSTRAINT CK_ProjectNotes_NoteText CHECK (LEN(NoteText) > 0),
    CONSTRAINT FK_ProjectNotes_Projects  FOREIGN KEY (ProjectId) REFERENCES dbo.Projects (ProjectId) ON DELETE CASCADE,
    CONSTRAINT FK_ProjectNotes_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_ProjectNotes_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE NONCLUSTERED INDEX IX_ProjectNotes_ProjectId
    ON dbo.ProjectNotes (ProjectId, CreatedAt DESC) WHERE IsDeleted = 0;
GO

/* =====================================================================
   8) Tenders
   Tender number is issued by the client, so it is unique PER client.
   ===================================================================== */
CREATE TABLE dbo.Tenders
(
    TenderId          INT IDENTITY(1,1) NOT NULL,
    TenderNumber      NVARCHAR(50)    NOT NULL,
    ClientId          INT             NOT NULL,
    ProjectId         INT             NULL,
    Title             NVARCHAR(250)   NOT NULL,
    Description       NVARCHAR(MAX)   NULL,
    IssueDate         DATE            NULL,
    SubmissionDate    DATE            NULL,            -- date we submitted
    ClosingDate       DATETIME2(0)    NULL,            -- deadline / expiry (local Muscat time)
    TenderValue       DECIMAL(18,3)   NULL,
    Currency          CHAR(3)         NOT NULL CONSTRAINT DF_Tenders_Currency DEFAULT ('OMR'),
    Status            TINYINT         NOT NULL CONSTRAINT DF_Tenders_Status DEFAULT (1),
                      -- 1=Open 2=Preparing 3=Submitted 4=UnderEvaluation 5=Awarded 6=Lost 7=Cancelled 8=Expired
    AssignedToId      NVARCHAR(450)   NULL,
    Notes             NVARCHAR(MAX)   NULL,
    CreatedAt         DATETIME2(3)    NOT NULL CONSTRAINT DF_Tenders_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy         NVARCHAR(450)   NULL,
    UpdatedAt         DATETIME2(3)    NULL,
    UpdatedBy         NVARCHAR(450)   NULL,
    IsDeleted         BIT             NOT NULL CONSTRAINT DF_Tenders_IsDeleted DEFAULT (0),
    DeletedAt         DATETIME2(3)    NULL,
    DeletedBy         NVARCHAR(450)   NULL,
    RowVersion        ROWVERSION      NOT NULL,
    CONSTRAINT PK_Tenders PRIMARY KEY CLUSTERED (TenderId),
    CONSTRAINT CK_Tenders_Status     CHECK (Status BETWEEN 1 AND 8),
    CONSTRAINT CK_Tenders_Value      CHECK (TenderValue IS NULL OR TenderValue >= 0),
    CONSTRAINT CK_Tenders_SoftDelete CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_Tenders_Clients    FOREIGN KEY (ClientId)     REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_Tenders_Projects   FOREIGN KEY (ProjectId)    REFERENCES dbo.Projects (ProjectId),
    CONSTRAINT FK_Tenders_AssignedTo FOREIGN KEY (AssignedToId) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Tenders_CreatedBy  FOREIGN KEY (CreatedBy)    REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Tenders_UpdatedBy  FOREIGN KEY (UpdatedBy)    REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Tenders_DeletedBy  FOREIGN KEY (DeletedBy)    REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE UNIQUE NONCLUSTERED INDEX UX_Tenders_Client_TenderNumber
    ON dbo.Tenders (ClientId, TenderNumber) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Tenders_ClosingDate
    ON dbo.Tenders (ClosingDate) INCLUDE (Status, ClientId, AssignedToId) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Tenders_ProjectId
    ON dbo.Tenders (ProjectId) WHERE ProjectId IS NOT NULL;
GO

/* =====================================================================
   9) Quotations
   Quotation number is OUR number => unique across the system.
   ===================================================================== */
CREATE TABLE dbo.Quotations
(
    QuotationId       INT IDENTITY(1,1) NOT NULL,
    QuotationNumber   NVARCHAR(50)    NOT NULL,
    ClientId          INT             NOT NULL,
    ContactId         INT             NULL,
    ProjectId         INT             NULL,
    TenderId          INT             NULL,
    Title             NVARCHAR(250)   NULL,
    SubmissionDate    DATE            NULL,            -- date sent
    QuotationValue    DECIMAL(18,3)   NULL,
    Currency          CHAR(3)         NOT NULL CONSTRAINT DF_Quotations_Currency DEFAULT ('OMR'),
    ValidUntil        DATE            NULL,            -- expiry date
    Status            TINYINT         NOT NULL CONSTRAINT DF_Quotations_Status DEFAULT (1),
                      -- 1=Draft 2=Sent 3=UnderReview 4=Accepted 5=Rejected 6=Expired 7=Cancelled
    AssignedToId      NVARCHAR(450)   NULL,
    Notes             NVARCHAR(MAX)   NULL,
    CreatedAt         DATETIME2(3)    NOT NULL CONSTRAINT DF_Quotations_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy         NVARCHAR(450)   NULL,
    UpdatedAt         DATETIME2(3)    NULL,
    UpdatedBy         NVARCHAR(450)   NULL,
    IsDeleted         BIT             NOT NULL CONSTRAINT DF_Quotations_IsDeleted DEFAULT (0),
    DeletedAt         DATETIME2(3)    NULL,
    DeletedBy         NVARCHAR(450)   NULL,
    RowVersion        ROWVERSION      NOT NULL,
    CONSTRAINT PK_Quotations PRIMARY KEY CLUSTERED (QuotationId),
    CONSTRAINT CK_Quotations_Status     CHECK (Status BETWEEN 1 AND 7),
    CONSTRAINT CK_Quotations_Value      CHECK (QuotationValue IS NULL OR QuotationValue >= 0),
    CONSTRAINT CK_Quotations_Dates      CHECK (ValidUntil IS NULL OR SubmissionDate IS NULL OR ValidUntil >= SubmissionDate),
    CONSTRAINT CK_Quotations_SoftDelete CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_Quotations_Clients    FOREIGN KEY (ClientId)     REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_Quotations_Contacts   FOREIGN KEY (ContactId)    REFERENCES dbo.Contacts (ContactId),
    CONSTRAINT FK_Quotations_Projects   FOREIGN KEY (ProjectId)    REFERENCES dbo.Projects (ProjectId),
    CONSTRAINT FK_Quotations_Tenders    FOREIGN KEY (TenderId)     REFERENCES dbo.Tenders (TenderId),
    CONSTRAINT FK_Quotations_AssignedTo FOREIGN KEY (AssignedToId) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Quotations_CreatedBy  FOREIGN KEY (CreatedBy)    REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Quotations_UpdatedBy  FOREIGN KEY (UpdatedBy)    REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Quotations_DeletedBy  FOREIGN KEY (DeletedBy)    REFERENCES dbo.ApplicationUsers (Id)
);
GO
CREATE UNIQUE NONCLUSTERED INDEX UX_Quotations_QuotationNumber
    ON dbo.Quotations (QuotationNumber) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Quotations_ClientId
    ON dbo.Quotations (ClientId) INCLUDE (Status, QuotationValue, ValidUntil) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Quotations_ValidUntil
    ON dbo.Quotations (ValidUntil) INCLUDE (Status, ClientId, AssignedToId) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Quotations_ProjectId
    ON dbo.Quotations (ProjectId) WHERE ProjectId IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_Quotations_TenderId
    ON dbo.Quotations (TenderId) WHERE TenderId IS NOT NULL;
GO

/* =====================================================================
   10) Activities  (THE core table - replaces the Excel columns)
   - One row = one communication (never merged).
   - Many activities per client per day are allowed.
   - PerformedById = employee credited in performance reports
     (may differ from CreatedBy, e.g. manager logs on behalf / import).
   ===================================================================== */
CREATE TABLE dbo.Activities
(
    ActivityId       INT IDENTITY(1,1) NOT NULL,
    ClientId         INT             NOT NULL,
    ContactId        INT             NULL,
    ActivityTypeId   INT             NOT NULL,
    ProjectId        INT             NULL,
    QuotationId      INT             NULL,
    TenderId         INT             NULL,
    ActivityDate     DATE            NOT NULL,          -- local date (original Excel date kept as-is)
    ActivityTime     TIME(0)         NULL,
    Subject          NVARCHAR(250)   NULL,
    Notes            NVARCHAR(MAX)   NULL,              -- full text, never truncated
    Outcome          NVARCHAR(500)   NULL,
    PerformedById    NVARCHAR(450)   NOT NULL,
    ImportBatchId    INT             NULL,
    SourceRowNumber  INT             NULL,              -- Excel row number for traceability
    CreatedAt        DATETIME2(3)    NOT NULL CONSTRAINT DF_Activities_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy        NVARCHAR(450)   NULL,
    UpdatedAt        DATETIME2(3)    NULL,
    UpdatedBy        NVARCHAR(450)   NULL,
    IsDeleted        BIT             NOT NULL CONSTRAINT DF_Activities_IsDeleted DEFAULT (0),
    DeletedAt        DATETIME2(3)    NULL,
    DeletedBy        NVARCHAR(450)   NULL,
    RowVersion       ROWVERSION      NOT NULL,
    CONSTRAINT PK_Activities PRIMARY KEY CLUSTERED (ActivityId),
    CONSTRAINT CK_Activities_Date       CHECK (ActivityDate >= '2000-01-01'),
    CONSTRAINT CK_Activities_Import     CHECK (ImportBatchId IS NOT NULL OR SourceRowNumber IS NULL),
    CONSTRAINT CK_Activities_SoftDelete CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_Activities_Clients       FOREIGN KEY (ClientId)       REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_Activities_Contacts      FOREIGN KEY (ContactId)      REFERENCES dbo.Contacts (ContactId),
    CONSTRAINT FK_Activities_ActivityTypes FOREIGN KEY (ActivityTypeId) REFERENCES dbo.ActivityTypes (ActivityTypeId),
    CONSTRAINT FK_Activities_Projects      FOREIGN KEY (ProjectId)      REFERENCES dbo.Projects (ProjectId),
    CONSTRAINT FK_Activities_Quotations    FOREIGN KEY (QuotationId)    REFERENCES dbo.Quotations (QuotationId),
    CONSTRAINT FK_Activities_Tenders       FOREIGN KEY (TenderId)       REFERENCES dbo.Tenders (TenderId),
    CONSTRAINT FK_Activities_PerformedBy   FOREIGN KEY (PerformedById)  REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Activities_ImportBatch   FOREIGN KEY (ImportBatchId)  REFERENCES dbo.ImportBatches (ImportBatchId),
    CONSTRAINT FK_Activities_CreatedBy     FOREIGN KEY (CreatedBy)      REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Activities_UpdatedBy     FOREIGN KEY (UpdatedBy)      REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Activities_DeletedBy     FOREIGN KEY (DeletedBy)      REFERENCES dbo.ApplicationUsers (Id)
);
GO
-- Client profile / timeline / "last contact date"
CREATE NONCLUSTERED INDEX IX_Activities_Client_Date
    ON dbo.Activities (ClientId, ActivityDate DESC, ActivityTime DESC)
    INCLUDE (ActivityTypeId, PerformedById) WHERE IsDeleted = 0;
-- Dashboard charts (per day / month / year, per type)
CREATE NONCLUSTERED INDEX IX_Activities_Date_Type
    ON dbo.Activities (ActivityDate, ActivityTypeId)
    INCLUDE (ClientId, PerformedById) WHERE IsDeleted = 0;
-- Employee performance
CREATE NONCLUSTERED INDEX IX_Activities_PerformedBy_Date
    ON dbo.Activities (PerformedById, ActivityDate)
    INCLUDE (ActivityTypeId, ClientId) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_Activities_ContactId   ON dbo.Activities (ContactId)     WHERE ContactId     IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_Activities_ProjectId   ON dbo.Activities (ProjectId)     WHERE ProjectId     IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_Activities_QuotationId ON dbo.Activities (QuotationId)   WHERE QuotationId   IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_Activities_TenderId    ON dbo.Activities (TenderId)      WHERE TenderId      IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_Activities_ImportBatch ON dbo.Activities (ImportBatchId) WHERE ImportBatchId IS NOT NULL;
GO

/* =====================================================================
   11) FollowUps  (scheduled next contacts + 24h reminder engine)

   Reminder rules implemented by data design:
   - ReminderAtUtc is PERSISTED computed = DueAtUtc - ReminderMinutesBefore
     => editing DueDate/DueTime (app recalculates DueAtUtc) automatically
        moves the reminder. The app also resets ReminderStatus to 1.
   - BackgroundService picks rows WHERE ReminderStatus = 1
     AND Status IN (1,4) AND ReminderAtUtc <= now  (index below).
   - Completed (2) / Cancelled (3) follow-ups are never picked.
   - Notifications.DedupKey (unique) makes double-sending impossible.

   Status        : 1=Pending 2=Completed 3=Cancelled 4=Overdue
   Priority      : 1=Low 2=Medium 3=High 4=Urgent
   ReminderStatus: 1=Scheduled 2=Sent 3=SentLate (created < 24h before due)
                   4=Skipped (completed/cancelled before reminder) 5=Failed
   ===================================================================== */
CREATE TABLE dbo.FollowUps
(
    FollowUpId             INT IDENTITY(1,1) NOT NULL,
    ClientId               INT             NOT NULL,
    ContactId              INT             NULL,
    FollowUpTypeId         INT             NOT NULL,     -- FK ActivityTypes (Call, Email, Visit...)
    SourceActivityId       INT             NULL,         -- activity that created this follow-up
    ParentFollowUpId       INT             NULL,         -- previous follow-up in the chain
    ProjectId              INT             NULL,
    QuotationId            INT             NULL,
    TenderId               INT             NULL,
    Description            NVARCHAR(1000)  NOT NULL,     -- what has to be done
    DueDate                DATE            NOT NULL,     -- local Muscat date
    DueTime                TIME(0)         NOT NULL CONSTRAINT DF_FollowUps_DueTime DEFAULT ('09:00'),
    TimeZoneId             VARCHAR(64)     NOT NULL CONSTRAINT DF_FollowUps_TimeZoneId DEFAULT ('Asia/Muscat'),
    DueAtUtc               DATETIME2(0)    NOT NULL,     -- calculated by the app from DueDate+DueTime+TimeZoneId
    AssignedToId           NVARCHAR(450)   NOT NULL,     -- responsible employee
    Priority               TINYINT         NOT NULL CONSTRAINT DF_FollowUps_Priority DEFAULT (2),
    Status                 TINYINT         NOT NULL CONSTRAINT DF_FollowUps_Status DEFAULT (1),
    ReminderMinutesBefore  INT             NOT NULL CONSTRAINT DF_FollowUps_ReminderMinutesBefore DEFAULT (1440),  -- 24h
    ReminderAtUtc          AS DATEADD(MINUTE, -ReminderMinutesBefore, DueAtUtc) PERSISTED,
    ReminderStatus         TINYINT         NOT NULL CONSTRAINT DF_FollowUps_ReminderStatus DEFAULT (1),
    ReminderSentAtUtc      DATETIME2(3)    NULL,
    CompletedAtUtc         DATETIME2(3)    NULL,
    CompletedById          NVARCHAR(450)   NULL,
    CompletedActivityId    INT             NULL,         -- activity logged when completing
    CompletionNotes        NVARCHAR(1000)  NULL,
    CancelledAtUtc         DATETIME2(3)    NULL,
    CancelledById          NVARCHAR(450)   NULL,
    CancelReason           NVARCHAR(500)   NULL,
    CreatedAt              DATETIME2(3)    NOT NULL CONSTRAINT DF_FollowUps_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy              NVARCHAR(450)   NULL,
    UpdatedAt              DATETIME2(3)    NULL,
    UpdatedBy              NVARCHAR(450)   NULL,
    IsDeleted              BIT             NOT NULL CONSTRAINT DF_FollowUps_IsDeleted DEFAULT (0),
    DeletedAt              DATETIME2(3)    NULL,
    DeletedBy              NVARCHAR(450)   NULL,
    RowVersion             ROWVERSION      NOT NULL,
    CONSTRAINT PK_FollowUps PRIMARY KEY CLUSTERED (FollowUpId),
    CONSTRAINT CK_FollowUps_Status         CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_FollowUps_Priority       CHECK (Priority BETWEEN 1 AND 4),
    CONSTRAINT CK_FollowUps_ReminderStatus CHECK (ReminderStatus BETWEEN 1 AND 5),
    CONSTRAINT CK_FollowUps_ReminderMins   CHECK (ReminderMinutesBefore BETWEEN 0 AND 43200),  -- max 30 days
    CONSTRAINT CK_FollowUps_Description    CHECK (LEN(LTRIM(RTRIM(Description))) > 0),
    CONSTRAINT CK_FollowUps_Completed      CHECK (Status <> 2 OR CompletedAtUtc IS NOT NULL),
    CONSTRAINT CK_FollowUps_Cancelled      CHECK (Status <> 3 OR CancelledAtUtc IS NOT NULL),
    CONSTRAINT CK_FollowUps_ReminderSent   CHECK (ReminderStatus NOT IN (2, 3) OR ReminderSentAtUtc IS NOT NULL),
    CONSTRAINT CK_FollowUps_NotSelfParent  CHECK (ParentFollowUpId IS NULL OR ParentFollowUpId <> FollowUpId),
    CONSTRAINT CK_FollowUps_SoftDelete     CHECK (IsDeleted = 0 OR DeletedAt IS NOT NULL),
    CONSTRAINT FK_FollowUps_Clients           FOREIGN KEY (ClientId)            REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_FollowUps_Contacts          FOREIGN KEY (ContactId)           REFERENCES dbo.Contacts (ContactId),
    CONSTRAINT FK_FollowUps_Type              FOREIGN KEY (FollowUpTypeId)      REFERENCES dbo.ActivityTypes (ActivityTypeId),
    CONSTRAINT FK_FollowUps_SourceActivity    FOREIGN KEY (SourceActivityId)    REFERENCES dbo.Activities (ActivityId),
    CONSTRAINT FK_FollowUps_Parent            FOREIGN KEY (ParentFollowUpId)    REFERENCES dbo.FollowUps (FollowUpId),
    CONSTRAINT FK_FollowUps_Projects          FOREIGN KEY (ProjectId)           REFERENCES dbo.Projects (ProjectId),
    CONSTRAINT FK_FollowUps_Quotations        FOREIGN KEY (QuotationId)         REFERENCES dbo.Quotations (QuotationId),
    CONSTRAINT FK_FollowUps_Tenders           FOREIGN KEY (TenderId)            REFERENCES dbo.Tenders (TenderId),
    CONSTRAINT FK_FollowUps_AssignedTo        FOREIGN KEY (AssignedToId)        REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_FollowUps_CompletedBy       FOREIGN KEY (CompletedById)       REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_FollowUps_CompletedActivity FOREIGN KEY (CompletedActivityId) REFERENCES dbo.Activities (ActivityId),
    CONSTRAINT FK_FollowUps_CancelledBy       FOREIGN KEY (CancelledById)       REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_FollowUps_CreatedBy         FOREIGN KEY (CreatedBy)           REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_FollowUps_UpdatedBy         FOREIGN KEY (UpdatedBy)           REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_FollowUps_DeletedBy         FOREIGN KEY (DeletedBy)           REFERENCES dbo.ApplicationUsers (Id)
);
GO
-- Reminder engine scan (tiny filtered index: only reminders still waiting)
CREATE NONCLUSTERED INDEX IX_FollowUps_ReminderScan
    ON dbo.FollowUps (ReminderAtUtc)
    INCLUDE (Status, DueAtUtc, AssignedToId, ClientId)
    WHERE ReminderStatus = 1 AND IsDeleted = 0;
-- Follow-ups screen: Today / Tomorrow / This week / Overdue for an employee
CREATE NONCLUSTERED INDEX IX_FollowUps_Assigned_Status_Due
    ON dbo.FollowUps (AssignedToId, Status, DueAtUtc)
    INCLUDE (ClientId, Priority) WHERE IsDeleted = 0;
-- Dashboard counters across all employees
CREATE NONCLUSTERED INDEX IX_FollowUps_Status_Due
    ON dbo.FollowUps (Status, DueAtUtc) WHERE IsDeleted = 0;
-- Client profile
CREATE NONCLUSTERED INDEX IX_FollowUps_Client_Due
    ON dbo.FollowUps (ClientId, DueAtUtc) INCLUDE (Status) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_FollowUps_SourceActivity    ON dbo.FollowUps (SourceActivityId)    WHERE SourceActivityId    IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_FollowUps_CompletedActivity ON dbo.FollowUps (CompletedActivityId) WHERE CompletedActivityId IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_FollowUps_Parent            ON dbo.FollowUps (ParentFollowUpId)    WHERE ParentFollowUpId    IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_FollowUps_ContactId         ON dbo.FollowUps (ContactId)           WHERE ContactId           IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_FollowUps_ProjectId         ON dbo.FollowUps (ProjectId)           WHERE ProjectId           IS NOT NULL;
GO

/* =====================================================================
   12) Notifications  (in-app bell; source for email / browser channels)
   DedupKey (UNIQUE) guarantees a notification is created only once:
     FU-REMIND:{FollowUpId}:{DueAtUtc yyyyMMddHHmm}:{UserId}
     QT-EXPIRY:{QuotationId}:{ValidUntil yyyyMMdd}:{UserId}
     TD-CLOSE:{TenderId}:{ClosingDate yyyyMMddHHmm}:{UserId}
   If a follow-up is rescheduled, DueAtUtc changes -> new key -> a new
   reminder is allowed for the new time, the old one can never repeat.
   NotificationType: 1=FollowUpReminder 2=FollowUpLateReminder
     3=FollowUpAssigned 4=FollowUpOverdue 5=QuotationExpiry
     6=TenderClosing 7=ImportCompleted 8=System
   ===================================================================== */
CREATE TABLE dbo.Notifications
(
    NotificationId    BIGINT IDENTITY(1,1) NOT NULL,
    UserId            NVARCHAR(450)   NOT NULL,          -- recipient
    NotificationType  TINYINT         NOT NULL,
    Title             NVARCHAR(250)   NOT NULL,
    Message           NVARCHAR(2000)  NOT NULL,
    Url               NVARCHAR(500)   NULL,              -- "open details" link
    ClientId          INT             NULL,
    FollowUpId        INT             NULL,
    ProjectId         INT             NULL,
    QuotationId       INT             NULL,
    TenderId          INT             NULL,
    EventAtLocal      DATETIME2(0)    NULL,              -- scheduled date/time shown in the list
    DedupKey          VARCHAR(200)    NOT NULL,
    IsRead            BIT             NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT (0),
    ReadAtUtc         DATETIME2(3)    NULL,
    CreatedAtUtc      DATETIME2(3)    NOT NULL CONSTRAINT DF_Notifications_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Notifications PRIMARY KEY CLUSTERED (NotificationId),
    CONSTRAINT UQ_Notifications_DedupKey UNIQUE (DedupKey),
    CONSTRAINT CK_Notifications_Type CHECK (NotificationType BETWEEN 1 AND 8),
    CONSTRAINT CK_Notifications_Read CHECK (IsRead = 0 OR ReadAtUtc IS NOT NULL),
    CONSTRAINT FK_Notifications_User       FOREIGN KEY (UserId)      REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT FK_Notifications_Clients    FOREIGN KEY (ClientId)    REFERENCES dbo.Clients (ClientId),
    CONSTRAINT FK_Notifications_FollowUps  FOREIGN KEY (FollowUpId)  REFERENCES dbo.FollowUps (FollowUpId),
    CONSTRAINT FK_Notifications_Projects   FOREIGN KEY (ProjectId)   REFERENCES dbo.Projects (ProjectId),
    CONSTRAINT FK_Notifications_Quotations FOREIGN KEY (QuotationId) REFERENCES dbo.Quotations (QuotationId),
    CONSTRAINT FK_Notifications_Tenders    FOREIGN KEY (TenderId)    REFERENCES dbo.Tenders (TenderId)
);
GO
-- Bell badge: unread count + latest list for a user
CREATE NONCLUSTERED INDEX IX_Notifications_User_Unread
    ON dbo.Notifications (UserId, IsRead, CreatedAtUtc DESC)
    INCLUDE (NotificationType, Title, ClientId, FollowUpId, EventAtLocal);
CREATE NONCLUSTERED INDEX IX_Notifications_FollowUpId
    ON dbo.Notifications (FollowUpId) WHERE FollowUpId IS NOT NULL;
GO

/* =====================================================================
   13) NotificationLogs  (one row per notification per channel)
   Channel: 1=InApp 2=Email 3=Browser
   Status : 1=Pending 2=Sent 3=Failed 4=Skipped (channel disabled / no email)
   UNIQUE (NotificationId, Channel) => an email can never be logged twice.
   ===================================================================== */
CREATE TABLE dbo.NotificationLogs
(
    NotificationLogId  BIGINT IDENTITY(1,1) NOT NULL,
    NotificationId     BIGINT          NOT NULL,
    Channel            TINYINT         NOT NULL,
    Status             TINYINT         NOT NULL CONSTRAINT DF_NotificationLogs_Status DEFAULT (1),
    Recipient          NVARCHAR(256)   NULL,             -- email address / browser endpoint label
    AttemptCount       INT             NOT NULL CONSTRAINT DF_NotificationLogs_AttemptCount DEFAULT (0),
    LastAttemptAtUtc   DATETIME2(3)    NULL,
    SentAtUtc          DATETIME2(3)    NULL,
    ErrorMessage       NVARCHAR(2000)  NULL,
    CreatedAtUtc       DATETIME2(3)    NOT NULL CONSTRAINT DF_NotificationLogs_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_NotificationLogs PRIMARY KEY CLUSTERED (NotificationLogId),
    CONSTRAINT UQ_NotificationLogs_Notification_Channel UNIQUE (NotificationId, Channel),
    CONSTRAINT CK_NotificationLogs_Channel  CHECK (Channel BETWEEN 1 AND 3),
    CONSTRAINT CK_NotificationLogs_Status   CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_NotificationLogs_Attempts CHECK (AttemptCount >= 0),
    CONSTRAINT CK_NotificationLogs_Sent     CHECK (Status <> 2 OR SentAtUtc IS NOT NULL),
    CONSTRAINT FK_NotificationLogs_Notifications FOREIGN KEY (NotificationId)
        REFERENCES dbo.Notifications (NotificationId) ON DELETE CASCADE
);
GO
-- Email/browser dispatcher: pending or failed (retry) rows only
CREATE NONCLUSTERED INDEX IX_NotificationLogs_Dispatch
    ON dbo.NotificationLogs (Status, Channel, LastAttemptAtUtc)
    INCLUDE (NotificationId, AttemptCount) WHERE Status IN (1, 3);
GO

/* =====================================================================
   14) PushSubscriptions  (Browser Web Push - optional channel)
   ===================================================================== */
CREATE TABLE dbo.PushSubscriptions
(
    PushSubscriptionId  INT IDENTITY(1,1) NOT NULL,
    UserId              NVARCHAR(450)  NOT NULL,
    Endpoint            NVARCHAR(800)  NOT NULL,
    P256dh              NVARCHAR(200)  NOT NULL,
    Auth                NVARCHAR(100)  NOT NULL,
    UserAgent           NVARCHAR(400)  NULL,
    CreatedAtUtc        DATETIME2(3)   NOT NULL CONSTRAINT DF_PushSubscriptions_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    LastUsedAtUtc       DATETIME2(3)   NULL,
    CONSTRAINT PK_PushSubscriptions PRIMARY KEY CLUSTERED (PushSubscriptionId),
    CONSTRAINT UQ_PushSubscriptions_Endpoint UNIQUE (Endpoint),
    CONSTRAINT FK_PushSubscriptions_User FOREIGN KEY (UserId) REFERENCES dbo.ApplicationUsers (Id) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX IX_PushSubscriptions_UserId ON dbo.PushSubscriptions (UserId);
GO

/* =====================================================================
   15) SystemSettings  (key/value, editable from the Settings screen)
   Secrets (SMTP password) are stored ENCRYPTED with ASP.NET Core
   Data Protection (IsEncrypted = 1). Never in code / appsettings.
   ===================================================================== */
CREATE TABLE dbo.SystemSettings
(
    SettingId      INT IDENTITY(1,1) NOT NULL,
    SettingKey     VARCHAR(100)    NOT NULL,
    SettingValue   NVARCHAR(MAX)   NULL,
    GroupName      VARCHAR(50)     NOT NULL,
    DataType       VARCHAR(20)     NOT NULL CONSTRAINT DF_SystemSettings_DataType DEFAULT ('string'),
    IsEncrypted    BIT             NOT NULL CONSTRAINT DF_SystemSettings_IsEncrypted DEFAULT (0),
    DescriptionEn  NVARCHAR(300)   NULL,
    DescriptionAr  NVARCHAR(300)   NULL,
    SortOrder      INT             NOT NULL CONSTRAINT DF_SystemSettings_SortOrder DEFAULT (0),
    UpdatedAt      DATETIME2(3)    NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedBy      NVARCHAR(450)   NULL,
    CONSTRAINT PK_SystemSettings PRIMARY KEY CLUSTERED (SettingId),
    CONSTRAINT UQ_SystemSettings_Key UNIQUE (SettingKey),
    CONSTRAINT CK_SystemSettings_DataType CHECK (DataType IN ('string', 'int', 'bool', 'decimal', 'json', 'secret', 'image', 'email', 'timezone', 'language')),
    CONSTRAINT CK_SystemSettings_Secret   CHECK (DataType <> 'secret' OR IsEncrypted = 1),
    CONSTRAINT FK_SystemSettings_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.ApplicationUsers (Id)
);
GO

/* =====================================================================
   16) AuditLogs  (who did what, when, old/new values as JSON)
   No FK on UserId on purpose: audit rows must survive anything.
   Action: Create, Update, Delete, SoftDelete, Restore, Login, LoginFailed,
           Logout, Export, Import, SettingsChanged, RoleChanged ...
   ===================================================================== */
CREATE TABLE dbo.AuditLogs
(
    AuditLogId    BIGINT IDENTITY(1,1) NOT NULL,
    UserId        NVARCHAR(450)   NULL,
    UserName      NVARCHAR(256)   NULL,
    Action        VARCHAR(50)     NOT NULL,
    EntityName    VARCHAR(100)    NULL,
    EntityId      VARCHAR(100)    NULL,
    OldValues     NVARCHAR(MAX)   NULL,
    NewValues     NVARCHAR(MAX)   NULL,
    IpAddress     VARCHAR(45)     NULL,               -- IPv4 / IPv6
    UserAgent     NVARCHAR(400)   NULL,
    CreatedAtUtc  DATETIME2(3)    NOT NULL CONSTRAINT DF_AuditLogs_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_AuditLogs PRIMARY KEY CLUSTERED (AuditLogId),
    CONSTRAINT CK_AuditLogs_OldValues CHECK (OldValues IS NULL OR ISJSON(OldValues) = 1),
    CONSTRAINT CK_AuditLogs_NewValues CHECK (NewValues IS NULL OR ISJSON(NewValues) = 1)
);
GO
CREATE NONCLUSTERED INDEX IX_AuditLogs_Entity    ON dbo.AuditLogs (EntityName, EntityId, CreatedAtUtc DESC);
CREATE NONCLUSTERED INDEX IX_AuditLogs_User_Date ON dbo.AuditLogs (UserId, CreatedAtUtc DESC);
CREATE NONCLUSTERED INDEX IX_AuditLogs_Date      ON dbo.AuditLogs (CreatedAtUtc DESC) INCLUDE (Action, EntityName);
GO

SET NOEXEC OFF;
GO
PRINT N'03_CoreTables.sql completed.';
GO
