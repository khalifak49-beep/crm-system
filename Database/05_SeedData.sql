/* =====================================================================
   CRM System - Phase 1
   File   : Database/05_SeedData.sql
   Purpose: Required reference data only (NO fake business data):
            roles, activity types, default system settings.
   Safe to re-run: inserts only what is missing.
   NOTE   : The first Admin user is created by the application at
            startup in Phase 2 (Identity must hash the password).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE CRMDatabase;
GO

/* ---------------------------------------------------------------------
   Roles (fixed Ids so the C# code can reference them)
   --------------------------------------------------------------------- */
MERGE dbo.Roles AS target
USING (VALUES
    (N'a1b2c3d4-0001-4000-8000-000000000001', N'Admin',         N'ADMIN',         N'Administrator',  N'مدير النظام',   N'Full access: all data, users and settings.'),
    (N'a1b2c3d4-0002-4000-8000-000000000002', N'SalesManager',  N'SALESMANAGER',  N'Sales Manager',  N'مدير المبيعات', N'Monitors all employees, activities, projects and team performance.'),
    (N'a1b2c3d4-0003-4000-8000-000000000003', N'SalesEmployee', N'SALESEMPLOYEE', N'Sales Employee', N'موظف مبيعات',  N'Logs activities and follow-ups for assigned clients.')
) AS source (Id, Name, NormalizedName, DisplayNameEn, DisplayNameAr, Description)
ON target.Id = source.Id
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name, NormalizedName, ConcurrencyStamp, DisplayNameEn, DisplayNameAr, Description)
    VALUES (source.Id, source.Name, source.NormalizedName, CONVERT(NVARCHAR(36), NEWID()),
            source.DisplayNameEn, source.DisplayNameAr, source.Description);
GO

/* ---------------------------------------------------------------------
   Activity types (fixed Ids 1..9 = C# enum ActivityTypeCode)
   IconName = key mapped to the Apple-style (SF Symbols look) icon set.
   Colors   = Apple system colors.
   --------------------------------------------------------------------- */
SET IDENTITY_INSERT dbo.ActivityTypes ON;

MERGE dbo.ActivityTypes AS target
USING (VALUES
    (1, 'COLD_CALL',          N'Cold Call',          N'مكالمة هاتفية',     'phone',          '#34C759', N'Cold Calls',    10),
    (2, 'EMAIL',              N'Email',              N'بريد إلكتروني',     'envelope',       '#007AFF', N'Emails',        20),
    (3, 'VISIT',              N'Visit',              N'زيارة',             'car_fill',       '#FF9500', N'Visit',         30),
    (4, 'FACILITY_TOUR',      N'Facility Tour',      N'جولة في المنشأة',   'building_2',     '#5AC8FA', N'Facility Tour', 40),
    (5, 'MEETING',            N'Meeting',            N'اجتماع',            'person_3',       '#5856D6', NULL,             50),
    (6, 'QUOTATION',          N'Quotation',          N'عرض سعر',           'doc_text',       '#30B0C7', NULL,             60),
    (7, 'PROJECT_DISCUSSION', N'Project Discussion', N'مناقشة مشروع',      'bubble_left_bubble_right', '#AF52DE', NULL,          70),
    (8, 'TENDER_FOLLOW_UP',   N'Tender Follow-up',   N'متابعة مناقصة',     'hammer',         '#FF2D55', NULL,             80),
    (9, 'OTHER',              N'Other',              N'أخرى',              'ellipsis_circle','#8E8E93', NULL,             90)
) AS source (ActivityTypeId, Code, NameEn, NameAr, IconName, ColorHex, ExcelColumnName, SortOrder)
ON target.ActivityTypeId = source.ActivityTypeId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ActivityTypeId, Code, NameEn, NameAr, IconName, ColorHex, ExcelColumnName, SortOrder, IsSystem, IsActive)
    VALUES (source.ActivityTypeId, source.Code, source.NameEn, source.NameAr, source.IconName,
            source.ColorHex, source.ExcelColumnName, source.SortOrder, 1, 1);

SET IDENTITY_INSERT dbo.ActivityTypes OFF;
GO
DBCC CHECKIDENT ('dbo.ActivityTypes', RESEED) WITH NO_INFOMSGS;
GO

/* ---------------------------------------------------------------------
   System settings (defaults - editable from Settings screen)
   --------------------------------------------------------------------- */
MERGE dbo.SystemSettings AS target
USING (VALUES
    -- Company / branding
    ('Company.Name',                        N'My Company',      'Company',       'string',   0, N'Company name shown in the system',            N'اسم الشركة الظاهر في النظام',           10),
    ('Company.NameAr',                      N'شركتي',           'Company',       'string',   0, N'Company name (Arabic)',                       N'اسم الشركة بالعربية',                   20),
    ('Company.LogoPath',                    N'',                'Company',       'image',    0, N'System logo (uploaded file path)',            N'شعار النظام',                           30),
    -- General
    ('System.DefaultLanguage',              N'en',              'General',       'language', 0, N'Default language (en / ar)',                  N'اللغة الافتراضية',                      10),
    ('System.TimeZone',                     N'Asia/Muscat',     'General',       'timezone', 0, N'System time zone',                            N'المنطقة الزمنية',                        20),
    ('System.Currency',                     N'OMR',             'General',       'string',   0, N'Default currency',                            N'العملة الافتراضية',                     30),
    ('System.DateFormat',                   N'dd/MM/yyyy',      'General',       'string',   0, N'Date display format',                         N'صيغة عرض التاريخ',                       40),
    -- SMTP (password stored encrypted by Data Protection - empty by default)
    ('Smtp.Host',                           N'',                'Smtp',          'string',   0, N'SMTP server host',                            N'خادم البريد SMTP',                       10),
    ('Smtp.Port',                           N'587',             'Smtp',          'int',      0, N'SMTP port',                                   N'منفذ SMTP',                              20),
    ('Smtp.EnableSsl',                      N'true',            'Smtp',          'bool',     0, N'Use SSL/TLS',                                 N'استخدام SSL/TLS',                        30),
    ('Smtp.Username',                       N'',                'Smtp',          'string',   0, N'SMTP username',                               N'اسم مستخدم SMTP',                        40),
    ('Smtp.Password',                       N'',                'Smtp',          'secret',   1, N'SMTP password (encrypted)',                   N'كلمة مرور SMTP (مشفرة)',                 50),
    ('Smtp.FromEmail',                      N'',                'Smtp',          'email',    0, N'Sender email address',                        N'البريد المرسل',                          60),
    ('Smtp.FromName',                       N'CRM System',      'Smtp',          'string',   0, N'Sender display name',                         N'اسم المرسل',                             70),
    -- Notification channels
    ('Notifications.InAppEnabled',          N'true',            'Notifications', 'bool',     0, N'Enable in-app notifications (bell)',          N'تفعيل تنبيهات الموقع',                   10),
    ('Notifications.EmailEnabled',          N'false',           'Notifications', 'bool',     0, N'Enable email notifications',                  N'تفعيل تنبيهات البريد الإلكتروني',        20),
    ('Notifications.BrowserEnabled',        N'true',            'Notifications', 'bool',     0, N'Enable browser notifications',                N'تفعيل إشعارات المتصفح',                  30),
    ('Notifications.PollIntervalSeconds',   N'30',              'Notifications', 'int',      0, N'Bell refresh interval (seconds)',             N'مدة تحديث جرس التنبيهات (ثواني)',        40),
    ('Notifications.MaxEmailAttempts',      N'3',               'Notifications', 'int',      0, N'Max email retry attempts',                    N'أقصى عدد لمحاولات إرسال البريد',         50),
    -- Reminders
    ('Reminders.FollowUpMinutesBefore',     N'1440',            'Reminders',     'int',      0, N'Default reminder before follow-up (minutes, 1440 = 24h)', N'مدة التذكير الافتراضية قبل المتابعة (دقائق)', 10),
    ('Reminders.SendLateReminderImmediately', N'true',          'Reminders',     'bool',     0, N'If created less than reminder time before due, notify immediately', N'إرسال تنبيه فوري إذا فات وقت التذكير', 20),
    ('Reminders.QuotationExpiryDaysBefore', N'3',               'Reminders',     'int',      0, N'Days before quotation expiry to notify',      N'أيام التنبيه قبل انتهاء عرض السعر',      30),
    ('Reminders.TenderClosingDaysBefore',   N'3',               'Reminders',     'int',      0, N'Days before tender closing to notify',        N'أيام التنبيه قبل إغلاق المناقصة',        40),
    ('Reminders.ScanIntervalSeconds',       N'60',              'Reminders',     'int',      0, N'Background reminder scan interval (seconds)', N'فترة فحص التذكيرات (ثواني)',             50),
    -- Security / users
    ('Security.EmployeesSeeOnlyAssignedClients', N'true',       'Security',      'bool',     0, N'Sales employees see only their assigned clients', N'الموظف يرى الشركات المسندة له فقط',  10),
    ('Security.PasswordMinLength',          N'8',               'Security',      'int',      0, N'Minimum password length',                     N'الحد الأدنى لطول كلمة المرور',           20),
    ('Security.LockoutMinutes',             N'15',              'Security',      'int',      0, N'Lockout duration after failed logins (minutes)', N'مدة القفل بعد محاولات الدخول الفاشلة', 30),
    ('Security.MaxFailedAccessAttempts',    N'5',               'Security',      'int',      0, N'Failed logins before lockout',                N'عدد المحاولات الفاشلة قبل القفل',        40)
) AS source (SettingKey, SettingValue, GroupName, DataType, IsEncrypted, DescriptionEn, DescriptionAr, SortOrder)
ON target.SettingKey = source.SettingKey
WHEN NOT MATCHED BY TARGET THEN
    INSERT (SettingKey, SettingValue, GroupName, DataType, IsEncrypted, DescriptionEn, DescriptionAr, SortOrder)
    VALUES (source.SettingKey, source.SettingValue, source.GroupName, source.DataType,
            source.IsEncrypted, source.DescriptionEn, source.DescriptionAr, source.SortOrder);
GO

PRINT N'05_SeedData.sql completed.';
SELECT 'Roles' AS [Table], COUNT(*) AS [Rows] FROM dbo.Roles
UNION ALL SELECT 'ActivityTypes', COUNT(*) FROM dbo.ActivityTypes
UNION ALL SELECT 'SystemSettings', COUNT(*) FROM dbo.SystemSettings;
GO
