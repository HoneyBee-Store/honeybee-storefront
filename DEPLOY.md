# Deploying

The app is deployable as-is. Everything it needs comes from environment
variables, so nothing secret lives in the repo.

## Required settings

| Variable | Example | Notes |
|---|---|---|
| `ConnectionStrings__Default` | `Server=…;Database=honeybee;User Id=…;Password=…;Encrypt=True` | Double underscore, not a colon |
| `Admin__Email` | `you@example.com` | Creates the admin on first start |
| `Admin__Password` | a long passphrase | At least 10 characters |
| `ASPNETCORE_ENVIRONMENT` | `Production` | |

The admin account is created once, on first startup. After that these two
`Admin__*` values are only used to check whether the account already exists —
changing them will **not** change the existing password.

## Recommended: Azure App Service + Azure SQL

Chosen because SQL Server rules out the cheap PaaS hosts (Railway, Render and
Fly offer PostgreSQL and MySQL, not SQL Server), and because of the uploads
caveat below.

1. **Create an Azure SQL Database** — Basic tier is enough. Note the connection
   string from *Settings → Connection strings → ADO.NET*.
2. **Allow Azure services** to reach the server: *Networking → Exception →
   "Allow Azure services and resources to access this server"*.
3. **Create an App Service** — Linux, .NET 10, any region (UAE North is nearest
   to Jordan; Europe is usually cheaper and only tens of milliseconds further).
4. **Add the settings above** under *Settings → Environment variables*.
5. **Deploy** — connect the GitHub repo under *Deployment Center*, pick
   `main`, and let it build. Or push the container built from the `Dockerfile`.

First start creates the schema, imports the catalogue, and creates the admin
account. Watch *Log stream* to confirm.

## Uploaded images survive on App Service — not everywhere

Product photos uploaded through the admin are written to
`wwwroot/img/products/`. On Azure App Service that path lives on persistent
storage, so uploads survive restarts and redeploys.

**On container hosts with an ephemeral filesystem (Railway, Render, Fly,
plain Docker) every uploaded photo is lost on the next deploy.** If you host
there instead, mount a volume at that path or move uploads to object storage
before relying on the admin's upload feature. The seeded photos are fine
either way — they are committed to the repo.

## Before going live

- [ ] Confirm HTTPS works and HTTP redirects to it (the app already sends HSTS
      outside Development).
- [ ] Sign in at `/Admin/Login` and change nothing — just confirm it works.
- [ ] Check the storefront in both languages.
- [ ] Set up a database backup schedule and **restore one once** to prove it
      works. An untested backup is not a backup.
- [ ] Point your domain at the app and leave the v1 site up until you are happy.

## A note on migrations

`Program.cs` runs `Database.MigrateAsync()` at startup. That is fine for one
instance. If you ever scale to two or more, move migrations to a deployment
step — two instances starting together will race each other.
