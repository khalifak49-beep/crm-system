# CRM System

نظام إدارة علاقات العملاء ثنائي اللغة (عربي / English) بديل لملف Excel.

Bilingual (Arabic RTL / English) CRM built with ASP.NET Core MVC (.NET 10), SQL Server, EF Core, Bootstrap 5 and Chart.js.

## Features

- Clients, contacts and activities. An activity can be a call, an email, a visit, a facility tour, and so on.
- Quick entry: record a new company and a new contact while you log an activity.
- Follow-ups. A reminder is sent automatically 24 hours before each one (Asia/Muscat), as an in-app, email and browser notification, never twice.
- Projects, quotations and tenders, with alerts before they expire.
- Reports, exportable to Excel, PDF (through printing) or paper.
- Excel import with a preview step and duplicate detection.
- Users and roles: Admin, Sales Manager and Sales Employee. Each user only sees the data their role allows.
- Settings, audit log and soft delete.

## Getting started (development)

1. **Create the database:**
   ```powershell
   cd Database
   .\RunAll.ps1
   ```
2. **Set the first admin password.** It is never stored in the code:
   ```powershell
   cd CrmSystem
   dotnet user-secrets set "SeedAdmin:Password" "YourStrongP@ss1"
   ```
3. **Run the app:**
   ```powershell
   dotnet run
   ```

## Documentation

- [docs/System_Guide.md](docs/System_Guide.md): how to use the system.
- [docs/Deployment_IIS.md](docs/Deployment_IIS.md): production deployment on IIS, including backups and HTTPS. The one-shot installer is `Deploy/Install-CrmOnIIS.ps1`.
- `docs/Phase1..3_*.md`: database design, project setup and UI.
