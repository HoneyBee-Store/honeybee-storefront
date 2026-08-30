# HoneyBee Storefront (v2)

The database-backed rebuild of [HoneyBee Shop](https://honeybee-store.github.io/HoneyBee_Shop.github.io/).
ASP.NET Core MVC on .NET 10, EF Core, SQL Server.

**v1 (the static site) stays live and untouched while this is built.** Nothing
here affects it.

## Status

Phase 1 of 5 — skeleton and data model. See the migration plan for the full
sequence.

- [x] Project, solution, packages
- [x] Domain model + `AppDbContext`
- [x] Seed importing the v1 catalogue (10 products, 2 pickup locations)
- [x] Initial migration
- [ ] Storefront pages rendering from the database
- [ ] Deploy to real hosting
- [ ] Admin panel (phase 2)
- [ ] Cart and order requests (phase 3)

## Running it locally

You need the .NET 10 SDK and SQL Server. LocalDB (ships with Visual Studio and
the SQL Server tools) is enough for development and is what this was verified
against.

**1. Set the connection string**

It is deliberately empty in `appsettings.json`, which is committed to git.
User Secrets keeps the real value out of the repo:

```bash
cd src/HoneyBee.Web
dotnet user-secrets set "ConnectionStrings:Default" "Server=(localdb)\MSSQLLocalDB;Database=honeybee;Trusted_Connection=True;MultipleActiveResultSets=true"
```

Already configured on this machine — you only need this on a new one.

**2. Run**

```bash
dotnet run --project src/HoneyBee.Web
```

The database is created, migrations apply, and the catalogue seeds on first
start. User Secrets only load in the Development environment, so set
`ASPNETCORE_ENVIRONMENT=Development` if you start the DLL directly.

In production, supply the connection string as an environment variable instead:
`ConnectionStrings__Default` (double underscore).

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
