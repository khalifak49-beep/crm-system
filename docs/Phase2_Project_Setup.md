# المرحلة الثانية — مشروع ASP.NET Core MVC + EF Core + Identity

> الحالة: ✅ مكتملة — البناء دون أي تحذير أو خطأ، و24/24 فحصاً ناجحاً، وتسجيل الدخول مُختبر فعلياً.

## 1. هيكل المشروع

```
mohammedproject/
├── CrmSystem.slnx
├── Database/                         ← المرحلة الأولى (SQL Scripts)
├── docs/
└── CrmSystem/
    ├── CrmSystem.csproj              .NET 10 + الحزم (إصدارات ثابتة)
    ├── Program.cs                    كل الإعدادات: EF، Identity، Cookies، DataProtection، Authorization
    ├── appsettings.json              Connection String + إعدادات (لا توجد فيه كلمات مرور)
    ├── Controllers/
    │   ├── AccountController.cs      تسجيل الدخول والخروج + تسجيلهما في AuditLogs
    │   └── HomeController.cs         صفحة حالة مؤقتة (تُستبدل بلوحة التحكم في المرحلة 3)
    ├── Data/
    │   ├── ApplicationDbContext.cs   DbContext + Audit تلقائي + Soft Delete
    │   ├── DbInitializer.cs          الأدوار + إنشاء أول Admin
    │   ├── DatabaseVerifier.cs       فحص تطابق الكود مع قاعدة البيانات (--verify-db)
    │   └── Configurations/           إعدادات Fluent API لكل جدول وView
    ├── Infrastructure/
    │   └── CurrentUser.cs            المستخدم الحالي (لـ CreatedBy و AuditLogs)
    ├── Models/
    │   ├── Common/                   IAuditable, ISoftDelete
    │   ├── Enums/CrmEnums.cs         كل الحالات (تطابق قيود CHECK في SQL)
    │   ├── Identity/                 ApplicationUser, ApplicationRole, AppRoles
    │   ├── Entities/                 16 كلاس للجداول
    │   ├── ReadModels/               4 كلاسات للـ Views
    │   └── ViewModels/
    └── Views/                        Login, AccessDenied, Home (مؤقتة)
```

## 2. الحزم (NuGet)

| الحزمة | الإصدار | الغرض |
|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.15 | EF Core مع SQL Server |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.15 | المستخدمون والأدوار |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.15 | حفظ مفاتيح التشفير في قاعدة البيانات |

الحزم التي تحتاجها المراحل القادمة (DevExtreme و ClosedXML للـ Excel ومكتبة PDF) ستُضاف في مرحلتها.

## 3. التشغيل

```powershell
cd C:\Users\khali\OneDrive\Desktop\mohammedproject\CrmSystem

# 1) كلمة مرور أول Admin تُحفظ في User Secrets، خارج المشروع وخارج الكود
#    (هذه الخطوة تمت على جهازك)
dotnet user-secrets set "SeedAdmin:Password" "YourStrongP@ss1"

# 2) فحص تطابق الكود مع قاعدة البيانات (اختياري)
dotnet run -- --verify-db

# 3) تشغيل الموقع
dotnet run --launch-profile http      # ثم افتح http://localhost:5156
```

أو من Visual Studio: افتح `CrmSystem.slnx` واضغط F5.

## 4. قواعد مهمة لكتابة الكود في المراحل القادمة

| الحالة | ماذا يحدث تلقائياً |
|---|---|
| `db.Clients.Add(x)` | تُملأ `CreatedAt` و `CreatedBy`، ويولّد SQL رمز الشركة `CL-00001`، ويُسجَّل صف في AuditLogs |
| تعديل كيان محمّل من قاعدة البيانات ثم `SaveChanges` | تُملأ `UpdatedAt` و `UpdatedBy`، وتُسجَّل الأعمدة التي تغيرت فقط (القيمة القديمة والجديدة) |
| `db.Remove(x)` | يتحول إلى حذف منطقي (`IsDeleted = 1`)، فيختفي السجل من كل الاستعلامات لكنه يبقى محفوظاً |
| رؤية السجلات المحذوفة | `.IgnoreQueryFilters()` |
| تحديد موعد متابعة | `followUp.SetSchedule(date, time)`: يحسب `DueAtUtc` بتوقيت مسقط، ويُعيد جدولة التذكير تلقائياً |
| تعديلان متزامنان على السجل نفسه | `DbUpdateConcurrencyException` بسبب عمود RowVersion |
| عمليات كبيرة مثل استيراد Excel | `db.AuditEnabled = false` ثم تسجيل صف Audit واحد يلخص العملية |
| الصلاحيات | `[Authorize(Roles = AppRoles.Admin)]` أو `[Authorize(Policy = AppPolicies.ManagerOrAdmin)]` |

## 5. الأمان المطبَّق

- كل الصفحات تتطلب تسجيل الدخول (`AuthorizeFilter` عام)، إلا ما عليه `[AllowAnonymous]`.
- حماية CSRF على كل طلب POST (`AutoValidateAntiforgeryToken`)، والطلب الذي لا يحمل Token يُرفض برمز 400.
- كلمات المرور مشفرة بـ PBKDF2 عبر Identity.
- قفل الحساب بعد 5 محاولات فاشلة لمدة 15 دقيقة.
- البريد الإلكتروني فريد لكل مستخدم.
- الحساب المعطّل (`IsActive = 0`) لا يستطيع الدخول.
- إعادة التحقق من صلاحية الجلسة كل 5 دقائق، فتعطيل مستخدم أو تغيير دوره يسري سريعاً.
- Cookie من نوع HttpOnly، مدتها 8 ساعات وتتجدد مع الاستخدام.
- مفاتيح التشفير محفوظة في SQL Server، فتبقى الجلسات وكلمة مرور SMTP المشفرة صالحة بعد إعادة تشغيل IIS.
- تسجيل محاولات الدخول الناجحة والفاشلة وتسجيل الخروج في AuditLogs مع عنوان IP.
