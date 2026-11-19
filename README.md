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

The database (`kaon.db`) is created automatically on first run, together with a starter menu
and sample food packages.

### Accounts

- **Kaon admin** (the Kaon team) invites carinderia owners and can switch accounts off. The admin gets a one-time private link (valid 48 hours) to send to the owner, and **the owner chooses their own password. The admin never sees any password.** Admins cannot see customers' personal details, orders or payment proofs.
- **Development only:** a demo admin (admin@kaon.local) and demo owner (owner@kaon.local) are created so you can log in on your own computer. Their passwords are in `Kaon/Program.cs`. They are never created in production.
- **Production:** no accounts exist until you set `Kaon__Admin__Email` and `Kaon__Admin__Password` (environment variables or Azure app settings). Never commit real passwords.

## Roles

- **Customer**: signs up on the site, orders from the daily menu or the Food Packages page, tracks the order.
- **Carinderia owner**: manages orders, menu, food packages and sizes, store settings, and the end-of-day summary.
- **Kaon admin**: invites owners by private link, switches owner accounts on or off.
- **Rider link**: planned.

## Project layout

- `Kaon/` the web app (Controllers, Views, Models, Data, wwwroot)
- `docs/` workbook, brand book, marketing plan and module list

Sample prices and dish names in the starter data are placeholders.
