# Hosting Kaon on Azure

Kaon is an ASP.NET Core MVC app on .NET 10 with a SQLite database. This guide is for whoever deploys it.

## 1. What the app does on its first start
- Creates the database and all tables by itself (migrations run at startup).
- Creates the **Kaon admin** from the settings below. There is no default admin on a real server.
- Starts **empty**: no carinderias, dishes or orders. Carinderias sign up at `/for-carinderias`.
- **Optional:** creates ONE sample carinderia ("Jamaica Foods") with 7 dishes, 6 food packages and one owner account, if the `Kaon__Seed__...` settings below are set. It only does this when the database has no carinderia yet, and never again.

## 2. Create the Azure resources
- App Service, **Linux**, runtime **.NET 10**.
- Plan: Free F1 for a demo, or Basic B1 for a pilot.
- Publish from Visual Studio (right-click the project, Publish, Azure, Azure App Service (Linux)) or connect the GitHub repository in Deployment Center.

## 3. App settings
Azure portal: Web App, Settings, Environment variables, App settings. Then restart the app.

| Name | Value | Required |
|---|---|---|
| `ConnectionStrings__KaonDb` | `Data Source=/home/data/kaon.db` | yes |
| `Kaon__DataDir` | `/home/data/files` | yes |
| `Kaon__Admin__Email` | the Kaon admin's email | yes |
| `Kaon__Admin__Password` | a strong password (ask the team, never put it in code) | yes |
| `Kaon__Seed__SampleCarinderia` | `true` | only for the sample carinderia |
| `Kaon__Seed__OwnerEmail` | the sample owner's email | with the sample |
| `Kaon__Seed__OwnerPassword` | the sample owner's password | with the sample |
| `Kaon__Demo__GCashNumber` | the sample carinderia's GCash number (optional) | no |
| `Kaon__Subscription__GCashNumber` | the number owners pay Kaon to (optional, can be set later in Admin, Billing, Settings) | no |

Everything under `/home` on Azure App Service survives restarts and new deployments.

## 4. Email (optional until set up)
Until SMTP is set, no automatic emails are sent. Invite and reset links still appear in a pop-up for the admin. To turn emails on, add `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__User`, `Email__Smtp__Password` and `Email__Smtp__From` (Brevo works).

## 5. After deploying
- Admin: `https://<your-app>.azurewebsites.net/admin/login`
- Owners log in at `/Account/Login`; each carinderia's customers use that carinderia's own link `/c/<slug>` or QR.
- Check `/` (landing page) and `/for-carinderias` (owner sign-up) open.

## 6. Known limits
- SQLite on one instance is fine for a pilot; use Azure SQL for many carinderias.
- Dish photos that owners upload are stored in `wwwroot/img/menu/uploads` and are lost on a new deployment (Azure Blob storage would fix it). The built-in sample dishes use images that ship with the app, so they always show.
- Back up `/home/data/kaon.db` regularly.
