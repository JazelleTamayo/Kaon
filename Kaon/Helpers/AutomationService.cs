using Kaon.Data;

namespace Kaon.Helpers;

// Runs the routine work (see Automation) a few minutes after the app starts, and then every 15 minutes.
public class AutomationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<AutomationService> _log;

    public AutomationService(IServiceScopeFactory scopes, IConfiguration config, ILogger<AutomationService> log)
    {
        _scopes = scopes;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KaonDbContext>();
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var baseUrl = _config["Kaon:PublicUrl"] ?? "https://localhost:7022";
                var r = await Automation.RunOnceAsync(db, email, baseUrl, DateTime.UtcNow, DateTime.Today);
                if (r.Reminders + r.MenusPosted + r.LinksCleaned > 0)
                    _log.LogInformation("Automation: {Reminders} reminder(s), {Menus} menu(s) posted, {Links} old link(s) cleaned.", r.Reminders, r.MenusPosted, r.LinksCleaned);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "The automatic routine work failed. It will try again later.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
