# HoneyBee Storefront (v2)

The database-backed rebuild of [HoneyBee Shop](https://honeybee-store.github.io/HoneyBee_Shop.github.io/).
ASP.NET Core MVC on .NET 10, EF Core, PostgreSQL.

**v1 (the static site) stays live and untouched while this is built.** Nothing
here affects it.

## Status

Phase 1 of 5 — skeleton and data model. See the migration plan for the full
sequence.

- [x] Project, solution, packages
- [x] Domain model + `AppDbContext`
- [x] Seed importing the v1 catalogue (10 products, 2 pickup locations)
- [x] Initial migration
- [x] Storefront pages rendering from the database
- [ ] Deploy to real hosting
- [x] Admin panel (phase 2)
- [x] Cart and order requests (phase 3)

## Running it locally

You need the .NET 10 SDK and PostgreSQL. The Windows installer from
<https://www.postgresql.org/download/windows/> includes the server and pgAdmin,
and installs it as a service that starts with the machine.

**1. Create the database**

During installation you set a password for the `postgres` user. Use `postgres`
to match the committed development connection string, or change the string to
match what you chose.

Then create an empty database called `honeybee` — either in pgAdmin, or:

```bash
psql -U postgres -c "CREATE DATABASE honeybee;"
```

The connection string for development lives in `appsettings.Development.json`
rather than User Secrets, so every way of launching the app agrees on one
database. It contains no real secret — a local database with a throwaway
password. Production supplies `ConnectionStrings__Default` as an environment
variable instead.

**2. Run**

```bash
dotnet run --project src/HoneyBee.Web
```

The database is created, migrations apply, and the catalogue seeds on first
start. User Secrets only load in the Development environment, so set
`ASPNETCORE_ENVIRONMENT=Development` if you start the DLL directly.

In production, supply the connection string as an environment variable instead:
`ConnectionStrings__Default` (double underscore).

## Order emails

Every order is emailed to the address in `Shop:OrderEmail`. The server, port
and sender are in `appsettings.json`; only the password is a secret.

Gmail will not accept an account password over SMTP. It needs an **App
password**, which requires 2-Step Verification:

1. Turn on 2-Step Verification: <https://myaccount.google.com/security>
2. Create an App password: <https://myaccount.google.com/apppasswords>
   Pick "Mail", name it "HoneyBee Shop". Google shows 16 characters.
3. Store it — with the spaces removed:

```
dotnet user-secrets set "Smtp:Password" "your16charapppassword" --project src/HoneyBee.Web
```

Restart the app, then check **Admin → Email** and press *Send a test email*.
That page reports the real SMTP error if one comes back, which is the quickest
way to tell an auth problem from a blocked port.

In production the password is the `Smtp__Password` environment variable.

Sending happens on a background task, so a slow mail server never delays a
customer at checkout. The flip side is that a failure is invisible on the
storefront — it goes to the log, and the Email screen is how you check.

With no password set, email is simply off: orders are still saved and still
open in WhatsApp.

## Layout

```
src/HoneyBee.Web/
├── Models/          Product, ProductImage, Order, OrderItem, PickupLocation
├── Data/            AppDbContext, DbSeeder
├── Migrations/      EF Core migrations — commit these
├── Controllers/     
├── Views/           
└── wwwroot/img/products/   product photos carried over from v1
```

## Notes worth keeping in mind

**No prices yet.** `Product.Price` is nullable and every seeded product has
`null`. The shop launches as an *order request*: customers choose products,
you confirm the total by phone. The money columns already exist so turning
prices on later is a data change, not a migration.

**Order lines snapshot their data.** `OrderItem` copies the product name and
unit price at checkout rather than joining. Without this, renaming a product
or changing its price silently rewrites what past orders say they cost.

**Pickup only.** No addresses, delivery zones or fees are modelled. Add them
when a customer actually asks.

**`FocalY` on `ProductImage`.** These are tall phone photos where the jar sits
high in frame, so a centred crop shows the carpet underneath. The values were
measured per photo and carried over from v1 — feed them into CSS
`object-position` when rendering.

**Identity is admin-only.** `AddIdentityCore` rather than `AddDefaultIdentity`,
so none of the register / forgot-password / 2FA UI is scaffolded. Customers
never sign in. Phase 2 adds one hand-written login page.

**Seeding is idempotent.** `DbSeeder` no-ops once rows exist, so it is safe to
leave wired up in `Program.cs`.

**`UseAppHost` is off.** This machine's application-control policy refuses to
launch freshly compiled `.exe` files, so the apphost `dotnet run` normally
produces was blocked with "Access is denied". The build now emits only
`HoneyBee.Web.dll`, which runs through the signed `dotnet` host. If you move to
a machine without that restriction you can delete the property; it changes
nothing else.

## Before deploying

- [ ] Move `db.Database.MigrateAsync()` out of `Program.cs` if ever running
      more than one instance — two starting together will race.
- [ ] Create the first admin user (no registration page exists by design).
- [ ] Set `ConnectionStrings__Default` in the host's environment.
- [ ] Confirm HTTPS and HSTS are on.
