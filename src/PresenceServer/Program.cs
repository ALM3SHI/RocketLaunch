using PresenceServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS) injects a PORT env var. Fall back to 5000 for
// local development so `dotnet run` still works without any env setup.
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// SignalR is what keeps a live, low-latency channel open with every
// running LauncherApp instance so "who's online" and invites feel instant.
builder.Services.AddSignalR();

// MVP-only: wide-open CORS so you can test the WPF client against a
// locally-hosted server without fighting the browser/HttpClient CORS
// rules. Lock this down (specific origin, credentials) before this ever
// touches a public server with real friend codes flowing through it.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLauncherApp", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("AllowLauncherApp");

app.MapGet("/", () => $"RocketLaunch Presence Server v1.0.0 is running on port {port}.");

app.MapHub<PresenceHub>("/presenceHub");

app.Run();
