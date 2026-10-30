# Stockify Server

ASP.NET Core 10 API for Stockify, a multi-company point-of-sale, inventory, invoicing and expenses platform. Endpoint reference: [API_DOCUMENTATION.md](API_DOCUMENTATION.md).

## Project structure

```
Stockify-Server/
├─ Program.cs          startup: database, services, auth, Swagger, middleware pipeline
├─ Controllers/        HTTP endpoints, one controller per resource
├─ Services/           business logic, one service per feature (OrderService, PosService, ...)
├─ Models/
│  ├─ Entities/        database tables (Order, Product, Branch, lookup tables, LookupCodes)
│  └─ Dtos/            request objects and other plain classes
├─ Data/               AppDbContext, Configurations/ (table mappings), Seeds/ (seed data)
├─ Migrations/         EF Core migrations
├─ Middleware/         error handling, suspended-company check, [RequirePermission]
└─ Utils/              helper functions and constants (date ranges, claims, JWT settings, ...)
```

A request goes **Controller → Service → AppDbContext**. Controllers stay thin; the rules live in services.

| To change | Go to |
|---|---|
| An endpoint | `Controllers/` |
| A business rule (orders, POS, stock, expenses, ...) | `Services/` |
| A table or column | `Models/Entities/` (class) and `Data/Configurations/` (mapping), then a migration |
| What a request accepts | `Models/Dtos/` |
| Seed data and lookup rows | `Data/Seeds/` |
| Startup, auth, Swagger, rate limits, service registration | `Program.cs` |

Fixed lists (order statuses, payment methods, sale types, ...) are lookup tables, not enums. They are seeded in `Data/Seeds/LookupSeeds.cs` and served by `GET /api/lookups`.

## Local setup

Run these in this folder:

1. Copy `.env.example` to `.env` and fill in the PostgreSQL connection string (database `StockifyDB`), `JwtSettings__Key` (32+ characters) and the first platform admin's email and password.
2. Create the database: `dotnet ef database update`
3. Run the API: `dotnet run` (Swagger at `/swagger` in Development).

To start over with an empty database, run `dotnet ef database drop --force`, then step 2 again.

The project targets .NET 10.

## Build

The folder holds both the solution and the project, so name the solution for solution-wide commands:

```sh
dotnet build Stockify.sln
```

## Adding things

- **Feature:** a service in `Services/` (register it in `Program.cs`), request classes in `Models/Dtos/`, a thin controller in `Controllers/` with `[RequirePermission("x.y")]`.
- **Table:** a class in `Models/Entities/`, a `DbSet` in `Data/AppDbContext.cs`, a mapping in `Data/Configurations/`, then `dotnet ef migrations add <Name>`.
- **Fixed list:** a lookup entity plus seed rows, never a C# `enum`.
- **Helper function:** `Utils/`.
