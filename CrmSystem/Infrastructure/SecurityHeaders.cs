namespace CrmSystem.Infrastructure;

/// <summary>
/// Adds browser security headers to every response:
///  - Content-Security-Policy: only this site's own scripts, styles, fonts and images
///    ('unsafe-inline' is needed for the small inline page scripts / style attributes)
///  - X-Content-Type-Options, X-Frame-Options / frame-ancestors (no click-jacking),
///    Referrer-Policy, Permissions-Policy.
/// All libraries are served locally from wwwroot/lib, so no external origins are allowed.
/// </summary>
public static class SecurityHeaders
{
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var h = context.Response.Headers;
                h["Content-Security-Policy"] = Csp;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "DENY";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h.Remove("X-Powered-By");
                h.Remove("Server");
                return Task.CompletedTask;
            });
            await next();
        });
}
