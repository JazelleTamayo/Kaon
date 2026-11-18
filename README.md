# Kaon

Digital pre-ordering, delivery and queue notification for local carinderias.
**Order ahead. Skip the line.**

ASP.NET Core MVC (.NET 10), Entity Framework Core, SQLite for local development.

## Run it

1. Install the .NET 10 SDK.
2. From this folder:

   ```bash
   dotnet run --project Kaon
   ```

3. Open the URL shown in the terminal (for example https://localhost:7022).

The database (`kaon.db`) is created automatically on first run, together with a starter menu,
sample food packages and a demo owner account (see `Kaon/Program.cs`).
Change the demo owner password before any real use.

## Roles

- **Customer**: signs up on the site, orders from the daily menu or the Food Packages page, tracks the order.
- **Carinderia owner**: manages orders, menu, food packages and sizes, store settings, and the end-of-day summary.
- **Kaon admin** and the **rider link**: planned.

## Project layout

- `Kaon/` the web app (Controllers, Views, Models, Data, wwwroot)
- `docs/` workbook, brand book, marketing plan and module list

Sample prices and dish names in the starter data are placeholders.
