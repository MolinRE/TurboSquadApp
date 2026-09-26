# Backend

The backend uses EF Core with PostgreSQL. Set `ConnectionStrings:Postgres` in local configuration or through `ConnectionStrings__Postgres`. Set the database credentials with `POSTGRES_USER` and `POSTGRES_PASSWORD`; keep secrets out of the repository.

After restoring packages, create and apply migrations with:

```bash
dotnet ef migrations add Initial --project back --output-dir Data/Migrations
dotnet ef database update --project back
```

`DatabaseSession` uses `AppDbContext` directly. The project does not add Unit of Work or a generic repository.

For authentication, set `Jwt__Key` to a random value of at least 32 characters. Optional settings are `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__ExpirationMinutes` (1–1440, default 60). The login endpoint is `POST /api/auth/login` with `username` and `password`; it returns a Bearer token. Registration is available at `POST /api/auth/register`.
