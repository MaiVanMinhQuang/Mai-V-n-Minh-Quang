# Important

Issues of this repository are tracked on https://github.com/aspnetboilerplate/aspnetboilerplate. Please create your issues on https://github.com/aspnetboilerplate/aspnetboilerplate/issues.

# Introduction

This is a template to create **ASP.NET Core MVC / Angular** based startup projects for [ASP.NET Boilerplate](https://aspnetboilerplate.com/Pages/Documents). It has 2 different versions:

1. [ASP.NET Core MVC & jQuery](https://aspnetboilerplate.com/Pages/Documents/Zero/Startup-Template-Core) (server rendered multi-page application).
2. [ASP.NET Core & Angular](https://aspnetboilerplate.com/Pages/Documents/Zero/Startup-Template-Angular) (single page application).
 
User Interface is based on [AdminLTE theme](https://github.com/ColorlibHQ/AdminLTE).
 
# Download

Create & download your project from https://aspnetboilerplate.com/Templates

# Screenshots

#### Sample Dashboard Page
![](_screenshots/module-zero-core-template-ui-home.png)

#### User Creation Modal
![](_screenshots/module-zero-core-template-ui-user-create-modal.png)

#### Login Page

![](_screenshots/module-zero-core-template-ui-login.png)

# Documentation

* [ASP.NET Core MVC & jQuery version.](https://aspnetboilerplate.com/Pages/Documents/Zero/Startup-Template-Core)
* [ASP.NET Core & Angular  version.](https://aspnetboilerplate.com/Pages/Documents/Zero/Startup-Template-Angular)

## TOEIC Reading application

The TOEIC Reading API and administrator web host are in
`aspnet-core/src/ToeicExam.Web.Host`; the administrator-only website is in `AdminWeb`.
Learners use the Flutter/Dart app in `../Frontend` without signing in. SQL Server is
the database provider. The default connection uses Windows Authentication and targets
the `toeic_reading` database on `localhost`; override it with the `ConnectionStrings__Default` environment
variable when the SQL Server instance is elsewhere.

On startup the host creates the database when permitted, creates any missing application
tables from `aspnet-core/src/ToeicExam.Web.Host/Sql/toeic_reading.sql`, and inserts the
demo administrator account if it does not already exist:

* Admin: `admin@toeic.local` / `Admin123!`

Change the demo passwords and JWT signing key before using this application outside a
local class/demo environment. Apply the existing ASP.NET Boilerplate identity migrations once, then start the API host
from the project root:

```powershell
dotnet run --project Backend\aspnet-core\src\ToeicExam.Migrator -- -q
dotnet run --project Backend\aspnet-core\src\ToeicExam.Web.Host
```

The solution targets .NET 9. If the machine only has a newer .NET runtime installed,
PowerShell can run these local demo commands with:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
```

Open the URL printed by ASP.NET Core. Admins can import the tagged TOEIC `.docx`, review
and edit questions, enter the correct answer for each question, and publish only after all
questions have four choices and an answer key. The supplied TOEIC Reading Test 10 document
does not contain correct answers, so it remains a draft until the admin enters the key.
The Flutter app lists published tests; each unauthenticated attempt receives a random
capability key that authorizes saving answers and retrieving only that attempt's result.

# License

[MIT](LICENSE).
