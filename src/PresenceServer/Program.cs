using PresenceServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/", () => "RocketLaunch Presence Server is running.");

app.MapHub<PresenceHub>("/presenceHub");

app.Run();
