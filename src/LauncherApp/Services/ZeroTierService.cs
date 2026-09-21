using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LauncherApp.Services;

/// <summary>
/// Wraps the local ZeroTier CLI and the ZeroTier Central API so the rest of
/// the app can just call JoinAsync / HostAsync and never worry about
/// shell commands or HTTP calls.
///
/// Requires:
///   1. ZeroTier One installed (service must be running).
///   2. A ZeroTier Central API token (free account at my.zerotier.com).
///      The token is stored in LocalProfile and read from there.
/// </summary>
public sealed class ZeroTierService
{
    // ZeroTier Central REST API base.
    private const string CentralBase = "https://api.zerotier.com/api/v1";

    // How long we poll waiting for a ZeroTier IP to be assigned after joining.
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan JoinPollInterval = TimeSpan.FromSeconds(2);

    private readonly string _apiToken;
    private readonly HttpClient _http;

    // Set after a successful Host or Join so the rest of the app can read it.
    public string? ActiveNetworkId { get; private set; }
    public string? LocalZeroTierIp { get; private set; }

    public ZeroTierService(string apiToken)
    {
        _apiToken = apiToken;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("token", _apiToken);
    }

    // -----------------------------------------------------------------------
    // Host side
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a brand-new private ZeroTier network and joins this machine
    /// to it.  Returns the network ID to embed in the invite payload.
    /// </summary>
    public async Task<string> CreateNetworkAndJoinAsync(string networkName, IProgress<string>? progress = null)
    {
        progress?.Report("Creating ZeroTier network...");

        // POST /network — creates a new network under the token's account.
        // Randomize the third octet (0-255) so multiple networks don't conflict
        var subnet = new Random().Next(1, 255);
        
        var body = new
        {
            name = networkName,
            config = new
            {
                v4AssignMode = new { zt = true },
                @private = true,
                routes = new[]
                {
                    new { target = $"10.147.{subnet}.0/24", via = (string?)null }
                },
                ipAssignmentPools = new[]
                {
                    new { ipRangeStart = $"10.147.{subnet}.1", ipRangeEnd = $"10.147.{subnet}.254" }
                }
            }
        };

        var response = await _http.PostAsync(
            $"{CentralBase}/network",
            new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var networkId = json["id"]!.GetValue<string>();

        progress?.Report($"Network created: {networkId}. Joining...");

        await CliJoinAsync(networkId);
        await AuthorizeThisMachineAsync(networkId, progress);

        var ip = await WaitForIpAsync(networkId, progress);
        ActiveNetworkId = networkId;
        LocalZeroTierIp = ip;

        progress?.Report($"Joined! Your ZeroTier IP: {ip}");
        return networkId;
    }

    // -----------------------------------------------------------------------
    // Guest side
    // -----------------------------------------------------------------------

    /// <summary>
    /// Joins an existing ZeroTier network (received in an invite) and waits
    /// until this machine has an IP on that network.
    /// </summary>
    public async Task<string> JoinNetworkAsync(string networkId, IProgress<string>? progress = null)
    {
        progress?.Report($"Joining network {networkId}...");

        await CliJoinAsync(networkId);

        // The host's Central account owns the network; they need to authorize
        // this machine.  We poll Central for our own membership status.
        var ip = await WaitForIpAsync(networkId, progress);
        ActiveNetworkId = networkId;
        LocalZeroTierIp = ip;

        progress?.Report($"Joined! Your ZeroTier IP: {ip}");
        return ip;
    }

    /// <summary>
    /// Authorizes all pending (unconfirmed) members of a network that this
    /// account owns.  Called by the Host after creating the network so that
    /// the guest's machine gets auto-approved without a manual step.
    /// </summary>
    public async Task AuthorizePendingMembersAsync(string networkId, IProgress<string>? progress = null)
    {
        var membersResp = await _http.GetAsync($"{CentralBase}/network/{networkId}/member");
        membersResp.EnsureSuccessStatusCode();

        var members = JsonNode.Parse(await membersResp.Content.ReadAsStringAsync())!.AsArray();

        foreach (var member in members)
        {
            var authorized = member!["config"]!["authorized"]?.GetValue<bool>() ?? false;
            if (!authorized)
            {
                var nodeId = member["nodeId"]!.GetValue<string>();
                progress?.Report($"Authorizing member {nodeId}...");
                await AuthorizeMemberAsync(networkId, nodeId);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------

    public async Task LeaveNetworkAsync(string networkId)
    {
        await RunCliAsync("leave", networkId);
        if (ActiveNetworkId == networkId)
        {
            ActiveNetworkId = null;
            LocalZeroTierIp = null;
        }
    }

    // -----------------------------------------------------------------------
    // Diagnostics
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true when the ZeroTier One Windows service is running.
    /// Uses ServiceController (no admin required) instead of zerotier-cli.
    /// </summary>
    public static bool IsZeroTierRunning()
    {
        // Method 1: check the Windows service (no admin needed)
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("ZeroTierOneService");
            return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch { }

        // Method 2: check if the process is running
        try
        {
            return System.Diagnostics.Process
                .GetProcessesByName("ZeroTier One").Length > 0
                || System.Diagnostics.Process
                .GetProcessesByName("zerotier-one_x64").Length > 0;
        }
        catch { }

        return false;
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    private static readonly string ZeroTierExePath = 
        @"C:\ProgramData\ZeroTier\One\zerotier-one_x64.exe";

    private async Task CliJoinAsync(string networkId)
    {
        var result = await RunCliAsync("join", networkId);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ZeroTier join failed (exit {result.ExitCode}): {result.Stderr}");
        }
    }

    /// <summary>
    /// Polls ZeroTier Central until this machine appears as a member with
    /// an assigned IP, or until JoinTimeout elapses.
    /// </summary>
    private async Task<string> WaitForIpAsync(string networkId, IProgress<string>? progress)
    {
        // First get our own node ID from the CLI so we can look up our membership.
        var info = await RunCliAsync("info");
        // Output: "200 info <nodeId> <version> <status>"
        var parts = info.Stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new InvalidOperationException("Could not read ZeroTier node ID.");

        var nodeId = parts[2];
        var deadline = DateTime.UtcNow + JoinTimeout;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(JoinPollInterval);
            progress?.Report("Waiting for IP assignment...");

            try
            {
                var resp = await _http.GetAsync($"{CentralBase}/network/{networkId}/member/{nodeId}");
                if (!resp.IsSuccessStatusCode) continue;

                var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
                var ipAddresses = json["config"]?["ipAssignments"]?.AsArray();

                if (ipAddresses is { Count: > 0 })
                {
                    return ipAddresses[0]!.GetValue<string>();
                }
            }
            catch
            {
                // transient HTTP error — keep polling
            }
        }

        throw new TimeoutException($"ZeroTier did not assign an IP within {JoinTimeout.TotalSeconds}s.");
    }

    private async Task AuthorizeThisMachineAsync(string networkId, IProgress<string>? progress)
    {
        var info = await RunCliAsync("info");
        var parts = info.Stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return;

        var nodeId = parts[2];
        progress?.Report($"Authorizing this machine ({nodeId})...");
        await AuthorizeMemberAsync(networkId, nodeId);
    }

    private async Task AuthorizeMemberAsync(string networkId, string nodeId)
    {
        string? ipToAssign = null;
        try
        {
            // Fetch the network's subnet to assign an IP explicitly
            var netResp = await _http.GetAsync($"{CentralBase}/network/{networkId}");
            if (netResp.IsSuccessStatusCode)
            {
                var netJson = JsonNode.Parse(await netResp.Content.ReadAsStringAsync())!;
                var ipStart = netJson["config"]?["ipAssignmentPools"]?[0]?["ipRangeStart"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(ipStart))
                {
                    var parts = ipStart.Split('.');
                    if (parts.Length == 4)
                    {
                        var baseIp = $"{parts[0]}.{parts[1]}.{parts[2]}";
                        // Pick a random IP to avoid collisions (since it's a tiny LAN, collision chance is ~0)
                        ipToAssign = $"{baseIp}.{new Random().Next(2, 250)}";
                    }
                }
            }
        }
        catch { /* fallback to auto-assign */ }

        object patch = ipToAssign != null
            ? new { config = new { authorized = true, ipAssignments = new[] { ipToAssign } } }
            : new { config = new { authorized = true } };

        var resp = await _http.PostAsync(
            $"{CentralBase}/network/{networkId}/member/{nodeId}",
            new StringContent(JsonSerializer.Serialize(patch), System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(params string[] args)
    {
        // On Windows, the CLI is just the main executable run with "-q"
        var allArgs = "-q " + string.Join(" ", args);
        
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ZeroTierExePath,
                Arguments = allArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return (proc.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
