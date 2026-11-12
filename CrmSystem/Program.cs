using CrmSystem.Data;
using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Localization;
using CrmSystem.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using CrmSystem.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Do not advertise the web server (IIS removes its own header via web.config removeServerHeader)
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ---------------------------------------------------------------------
// Database (EF Core -> SQL Server, tables created by Database/*.sql)
// ---------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("CRMDatabase")
    ?? throw new InvalidOperationException("Connection string 'CRMDatabase' not found in appsettings.json.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IAppSettings, AppSettingsProvider>();

// Reminders, notifications, e-mail (see Services/)
builder.Services.AddSingleton<ReminderSignal>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<ReminderEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReminderEngine>());
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ReportService>();

// ---------------------------------------------------------------------
// Localization: English / Arabic from Resources/i18n/*.json
// Order: language cookie -> System.DefaultLanguage setting
// ---------------------------------------------------------------------
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(AppCultures.EnglishCulture, AppCultures.EnglishCulture);
    options.SupportedCultures = AppCultures.Supported;
    options.SupportedUICultures = AppCultures.Supported;
    options.FallBackToParentCultures = true;
    options.FallBackToParentUICultures = true;
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new CookieRequestCultureProvider(),
        new SettingsDefaultCultureProvider()
    };
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        sql.CommandTimeout(60);
        sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    });
    // Required navigations to soft-deleted parents are intentional (history is kept).
    options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// ---------------------------------------------------------------------
// ASP.NET Core Identity (passwords hashed with PBKDF2, lockout, unique email)
// ---------------------------------------------------------------------
var identitySection = builder.Configuration.GetSection("Identity");

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Stores.MaxLengthForKeys = 128;

        options.Password.RequiredLength = identitySection.GetValue("PasswordMinLength", 8);
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = identitySection.GetValue("MaxFailedAccessAttempts", 5);
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(identitySection.GetValue("LockoutMinutes", 15));

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

// Password / lockout values from Settings (Security group)
builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<IdentityOptions>, IdentityOptionsFromSettings>();

// AJAX requests send the CSRF token in this header (see wwwroot/js/app.js)
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

// Validate the security stamp on every request: deactivating a user, resetting a password
// or changing a role signs that user out IMMEDIATELY (one primary-key lookup per request).
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "CrmSystem.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Secure-only cookies when the site requires HTTPS; same-as-request on plain-http intranets
    options.Cookie.SecurePolicy = builder.Configuration.GetValue("App:RequireHttps", false)
        ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// Keys stored in SQL Server (DataProtectionKeys table): cookies and encrypted settings
// (SMTP password) stay valid after IIS app-pool recycles and restarts.
builder.Services.AddDataProtection()
    .SetApplicationName("CrmSystem")
    .PersistKeysToDbContext<ApplicationDbContext>();

// ---------------------------------------------------------------------
// Authorization: every page requires login unless marked [AllowAnonymous]
// ---------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.AdminOnly, p => p.RequireRole(AppRoles.Admin));
    options.AddPolicy(AppPolicies.ManagerOrAdmin, p => p.RequireRole(AppRoles.Admin, AppRoles.SalesManager));
    options.AddPolicy(AppPolicies.AllStaff, p => p.RequireRole(AppRoles.Admin, AppRoles.SalesManager, AppRoles.SalesEmployee));
});

// Output Arabic as real characters instead of &#x...; entities (still safely HTML-encoded)
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()); // CSRF protection on every POST
    options.Filters.Add<AjaxExceptionFilter>();                       // JSON errors for AJAX calls
})
.AddViewLocalization()
.AddDataAnnotationsLocalization(o =>
    o.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));
builder.Services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<MvcOptions>, ModelBindingMessages>();

// Excel import uploads (default limit is 30 MB; keep it explicit)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

var app = builder.Build();

// ---------------------------------------------------------------------
// "dotnet run -- --verify-db" : verify model <-> database and exit
// ---------------------------------------------------------------------
if (args.Contains("--verify-db"))
{
    var exitCode = await DatabaseVerifier.RunAsync(app.Services);
    Environment.Exit(exitCode);
}

await DbInitializer.InitializeAsync(app.Services);

// ---------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// HTTPS: enable "App:RequireHttps" once the IIS site has an https binding + certificate.
// (On an internal network without a certificate the site keeps working over http.)
if (app.Configuration.GetValue("App:RequireHttps", false))
{
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();
app.UseRequestLocalization();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// GET /health : for IIS / monitoring. No sensitive details, no login required.
//   200 {"status":"ok"} when the database answers and the reminder engine scanned recently.
app.MapGet("/health", async (ApplicationDbContext db, ReminderEngine engine, CancellationToken ct) =>
{
    var database = await db.Database.CanConnectAsync(ct);
    var reminders = engine.LastSuccessUtc.HasValue && engine.LastSuccessUtc > DateTime.UtcNow.AddMinutes(-15);
    var body = new
    {
        status = database && reminders ? "ok" : "degraded",
        database = database ? "ok" : "unreachable",
        reminderEngine = reminders ? "ok" : (engine.LastSuccessUtc.HasValue ? "stale" : "starting"),
        lastReminderScanUtc = engine.LastSuccessUtc
    };
    return database ? Results.Json(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
