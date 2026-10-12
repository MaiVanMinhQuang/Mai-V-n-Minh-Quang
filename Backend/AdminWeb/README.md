# TOEIC Reading Practice

This static browser app is the administrator-only website. It is served by
`ToeicExam.Web.Host` so requests use the same origin as the API. Learners use the
Flutter app in `Frontend` and do not sign in.

```powershell
dotnet run --project Backend\aspnet-core\src\ToeicExam.Migrator -- -q
dotnet run --project Backend\aspnet-core\src\ToeicExam.Web.Host
```

Open the HTTPS URL printed by the host. The first launch initializes the `toeic_reading`
SQL Server database and creates the administrator account if needed:

* Admin: `admin@toeic.local` / `Admin123!`

For a different SQL Server, set `ConnectionStrings__Default` before starting the backend.
This admin website uses same-origin `/api` requests and does not need a separate
development server or package installation.
