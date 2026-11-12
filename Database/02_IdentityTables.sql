/* =====================================================================
   CRM System - Phase 1
   File   : Database/02_IdentityTables.sql
   Purpose: ASP.NET Core Identity tables (users, roles, claims, logins,
            tokens) with custom table names and extra CRM columns.
   Notes  : The structure matches ASP.NET Core Identity (schema v1) exactly,
            so in Phase 2 EF Core maps to these tables with:
              builder.Entity<ApplicationUser>().ToTable("ApplicationUsers");
              builder.Entity<ApplicationRole>().ToTable("Roles");
              ... and options.Stores.MaxLengthForKeys = 128;
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
GO

USE CRMDatabase;
GO

/* Safety guard: do not run twice on an existing schema. */
IF OBJECT_ID(N'dbo.ApplicationUsers', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'Identity tables already exist. Script 02 skipped (nothing changed).', 10, 1);
    SET NOEXEC ON;
END
GO

/* ---------------------------------------------------------------------
   Roles  (Admin, SalesManager, SalesEmployee)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Roles
(
    Id                 NVARCHAR(450)  NOT NULL,
    Name               NVARCHAR(256)  NULL,
    NormalizedName     NVARCHAR(256)  NULL,
    ConcurrencyStamp   NVARCHAR(MAX)  NULL,
    -- CRM extra columns
    DisplayNameEn      NVARCHAR(100)  NULL,
    DisplayNameAr      NVARCHAR(100)  NULL,
    Description        NVARCHAR(500)  NULL,
    CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED (Id)
);
GO
CREATE UNIQUE NONCLUSTERED INDEX RoleNameIndex
    ON dbo.Roles (NormalizedName) WHERE NormalizedName IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   ApplicationUsers
   --------------------------------------------------------------------- */
CREATE TABLE dbo.ApplicationUsers
(
    Id                          NVARCHAR(450)     NOT NULL,
    UserName                    NVARCHAR(256)     NULL,
    NormalizedUserName          NVARCHAR(256)     NULL,
    Email                       NVARCHAR(256)     NULL,
    NormalizedEmail             NVARCHAR(256)     NULL,
    EmailConfirmed              BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_EmailConfirmed DEFAULT (0),
    PasswordHash                NVARCHAR(MAX)     NULL,   -- PBKDF2 hash produced by Identity (never plain text)
    SecurityStamp               NVARCHAR(MAX)     NULL,
    ConcurrencyStamp            NVARCHAR(MAX)     NULL,
    PhoneNumber                 NVARCHAR(MAX)     NULL,
    PhoneNumberConfirmed        BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_PhoneNumberConfirmed DEFAULT (0),
    TwoFactorEnabled            BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_TwoFactorEnabled DEFAULT (0),
    LockoutEnd                  DATETIMEOFFSET(7) NULL,
    LockoutEnabled              BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_LockoutEnabled DEFAULT (1),
    AccessFailedCount           INT               NOT NULL CONSTRAINT DF_ApplicationUsers_AccessFailedCount DEFAULT (0),

    -- CRM extra columns
    FullName                    NVARCHAR(150)     NOT NULL CONSTRAINT DF_ApplicationUsers_FullName DEFAULT (N''),
    FullNameAr                  NVARCHAR(150)     NULL,
    JobTitle                    NVARCHAR(100)     NULL,
    ManagerId                   NVARCHAR(450)     NULL,   -- Sales Manager of this employee
    PreferredLanguage           VARCHAR(5)        NOT NULL CONSTRAINT DF_ApplicationUsers_PreferredLanguage DEFAULT ('en'),
    ProfileImagePath            NVARCHAR(400)     NULL,
    IsActive                    BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_IsActive DEFAULT (1),
    ReceiveEmailNotifications   BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_ReceiveEmail DEFAULT (1),
    ReceiveBrowserNotifications BIT               NOT NULL CONSTRAINT DF_ApplicationUsers_ReceiveBrowser DEFAULT (1),
    LastLoginAtUtc              DATETIME2(3)      NULL,
    CreatedAt                   DATETIME2(3)      NOT NULL CONSTRAINT DF_ApplicationUsers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy                   NVARCHAR(450)     NULL,
    UpdatedAt                   DATETIME2(3)      NULL,
    UpdatedBy                   NVARCHAR(450)     NULL,

    CONSTRAINT PK_ApplicationUsers PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ApplicationUsers_Manager FOREIGN KEY (ManagerId) REFERENCES dbo.ApplicationUsers (Id),
    CONSTRAINT CK_ApplicationUsers_PreferredLanguage CHECK (PreferredLanguage IN ('en', 'ar')),
    CONSTRAINT CK_ApplicationUsers_AccessFailedCount CHECK (AccessFailedCount >= 0)
);
GO
CREATE UNIQUE NONCLUSTERED INDEX UserNameIndex
    ON dbo.ApplicationUsers (NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
CREATE NONCLUSTERED INDEX EmailIndex
    ON dbo.ApplicationUsers (NormalizedEmail);
CREATE NONCLUSTERED INDEX IX_ApplicationUsers_ManagerId
    ON dbo.ApplicationUsers (ManagerId) WHERE ManagerId IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   UserRoles (many-to-many Users <-> Roles)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.UserRoles
(
    UserId  NVARCHAR(450) NOT NULL,
    RoleId  NVARCHAR(450) NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY CLUSTERED (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.ApplicationUsers (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX IX_UserRoles_RoleId ON dbo.UserRoles (RoleId);
GO

/* ---------------------------------------------------------------------
   UserClaims
   --------------------------------------------------------------------- */
CREATE TABLE dbo.UserClaims
(
    Id          INT IDENTITY(1,1) NOT NULL,
    UserId      NVARCHAR(450)     NOT NULL,
    ClaimType   NVARCHAR(MAX)     NULL,
    ClaimValue  NVARCHAR(MAX)     NULL,
    CONSTRAINT PK_UserClaims PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_UserClaims_Users FOREIGN KEY (UserId) REFERENCES dbo.ApplicationUsers (Id) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX IX_UserClaims_UserId ON dbo.UserClaims (UserId);
GO

/* ---------------------------------------------------------------------
   UserLogins (external logins - kept for Identity compatibility)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.UserLogins
(
    LoginProvider        NVARCHAR(128) NOT NULL,
    ProviderKey          NVARCHAR(128) NOT NULL,
    ProviderDisplayName  NVARCHAR(MAX) NULL,
    UserId               NVARCHAR(450) NOT NULL,
    CONSTRAINT PK_UserLogins PRIMARY KEY CLUSTERED (LoginProvider, ProviderKey),
    CONSTRAINT FK_UserLogins_Users FOREIGN KEY (UserId) REFERENCES dbo.ApplicationUsers (Id) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX IX_UserLogins_UserId ON dbo.UserLogins (UserId);
GO

/* ---------------------------------------------------------------------
   UserTokens
   --------------------------------------------------------------------- */
CREATE TABLE dbo.UserTokens
(
    UserId         NVARCHAR(450) NOT NULL,
    LoginProvider  NVARCHAR(128) NOT NULL,
    Name           NVARCHAR(128) NOT NULL,
    Value          NVARCHAR(MAX) NULL,
    CONSTRAINT PK_UserTokens PRIMARY KEY CLUSTERED (UserId, LoginProvider, Name),
    CONSTRAINT FK_UserTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.ApplicationUsers (Id) ON DELETE CASCADE
);
GO

/* ---------------------------------------------------------------------
   RoleClaims (fine-grained permissions per role)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.RoleClaims
(
    Id          INT IDENTITY(1,1) NOT NULL,
    RoleId      NVARCHAR(450)     NOT NULL,
    ClaimType   NVARCHAR(MAX)     NULL,
    ClaimValue  NVARCHAR(MAX)     NULL,
    CONSTRAINT PK_RoleClaims PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RoleClaims_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id) ON DELETE CASCADE
);
GO
CREATE NONCLUSTERED INDEX IX_RoleClaims_RoleId ON dbo.RoleClaims (RoleId);
GO

/* ---------------------------------------------------------------------
   DataProtectionKeys
   Persists ASP.NET Core Data Protection keys in SQL Server so that:
   - encrypted settings (SMTP password) stay readable after IIS restarts,
   - login cookies survive app-pool recycles.
   Matches Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.DataProtectionKeys
(
    Id            INT IDENTITY(1,1) NOT NULL,
    FriendlyName  NVARCHAR(MAX)     NULL,
    Xml           NVARCHAR(MAX)     NULL,
    CONSTRAINT PK_DataProtectionKeys PRIMARY KEY CLUSTERED (Id)
);
GO

SET NOEXEC OFF;
GO
PRINT N'02_IdentityTables.sql completed.';
GO
