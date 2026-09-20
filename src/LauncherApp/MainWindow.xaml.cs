using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LauncherApp.Models;
using LauncherApp.Services;

namespace LauncherApp;

public partial class MainWindow : Window
{
    private readonly LocalProfile _profile;
    private readonly PresenceClient _presence;
    private readonly GameLauncherService _gameLauncher;
    private readonly ObservableCollection<Friend> _friends;
    private readonly DispatcherTimer _pollTimer;

    // Phase 2: ZeroTier — created lazily after token is confirmed.
    private ZeroTierService? _zeroTier;

    // Phase 4: auto-update check on launch.
    private readonly UpdateService _updater = new();

    public MainWindow()
    {
        InitializeComponent();

        _profile      = LocalProfile.LoadOrCreate();
        _presence     = new PresenceClient();
        _gameLauncher = new GameLauncherService();
        _friends      = new ObservableCollection<Friend>(_profile.Friends);

        FriendsList.ItemsSource   = _friends;
        MyFriendCodeText.Text     = _profile.MyFriendCode;

        _presence.InviteReceived += OnInviteReceived;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _pollTimer.Tick += async (_, _) => await RefreshFriendStatusesAsync();

        Loaded  += MainWindow_Loaded;
        Closing += (_, _) => _profile.Save();
    }

    // ───────────────────────────────────────── Init
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshEmptyHint();
        await ConnectToPresenceServerAsync();
        UpdateTokenSetupVisibility();
        _gameLauncher.DetectInstall();

        // Check for updates in the background — never blocks startup.
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var info = await _updater.CheckForUpdateAsync();
        if (info is null) return;

        var result = MessageBox.Show(
            $"RocketLaunch {info.Version} is available!\n\nDownload and install now?",
            "Update Available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
            await _updater.DownloadAndInstallAsync(info);
    }


    private async Task ConnectToPresenceServerAsync()
    {
        try
        {
            await _presence.ConnectAsync(_profile.MyFriendCode, _profile.MyDisplayName);
            ConnectionStatusText.Text = $"Online  ·  {_profile.MyDisplayName}";
            _pollTimer.Start();
            await RefreshFriendStatusesAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"Offline (server unreachable: {ex.Message})";
        }
    }

    private void UpdateTokenSetupVisibility()
    {
        TokenSetupPanel.Visibility = string.IsNullOrWhiteSpace(_profile.ZeroTierApiToken)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(_profile.ZeroTierApiToken))
            _zeroTier = new ZeroTierService(_profile.ZeroTierApiToken);
    }

    // ───────────────────────────────────────── Friend poll
    private async Task RefreshFriendStatusesAsync()
    {
        foreach (var friend in _friends)
        {
            var (isOnline, displayName) = await _presence.CheckStatusDetailedAsync(friend.FriendCode);
            friend.IsOnline = isOnline;

            // Update display name when we first see the friend online.
            if (isOnline && displayName != friend.FriendCode)
                friend.DisplayName = displayName;
        }

        // Rebind cheaply (MVP list sizes).
        FriendsList.ItemsSource = null;
        FriendsList.ItemsSource = _friends;
        RefreshEmptyHint();
    }

    private void RefreshEmptyHint()
    {
        EmptyFriendsHint.Visibility = _friends.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ───────────────────────────────────────── Add friend
    private void AddFriendButton_Click(object sender, RoutedEventArgs e)
    {
        var code = AddFriendCodeBox.Text.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) || _friends.Any(f => f.FriendCode == code))
            return;

        var friend = new Friend { FriendCode = code, DisplayName = code, IsOnline = false };
        _friends.Add(friend);
        _profile.Friends.Add(friend);
        _profile.Save();

        AddFriendCodeBox.Clear();
        RefreshEmptyHint();
    }

    // ───────────────────────────────────────── ZeroTier token setup
    private void SaveTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var token = ApiTokenBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(token)) return;

        if (!ZeroTierService.IsZeroTierRunning())
        {
            MessageBox.Show(
                "ZeroTier One doesn't appear to be running on this machine.\n\n" +
                "Download and install it from zerotier.com/download, start the service, then try again.",
                "RocketLaunch — ZeroTier Not Found",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile.ZeroTierApiToken = token;
        _profile.Save();
        _zeroTier = new ZeroTierService(token);
        TokenSetupPanel.Visibility = Visibility.Collapsed;

        MessageBox.Show("ZeroTier token saved. You're ready to host or join games!", "RocketLaunch");
    }

    // ───────────────────────────────────────── Host game
    private async void HostGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_zeroTier is null)
        {
            MessageBox.Show("Please save your ZeroTier API token first.", "RocketLaunch");
            return;
        }

        HostGameButton.IsEnabled  = false;
        JoinByIdButton.IsEnabled  = false;
        HostJoinRow.Visibility    = Visibility.Collapsed;
        SessionPanel.Visibility   = Visibility.Visible;
        SessionTitleText.Text     = "Hosting Session";
        SessionStatusText.Text    = "Setting up network...";
        LaunchGameButton.IsEnabled = false;
        LeaveNetworkButton.IsEnabled = false;

        var progress = new Progress<string>(msg => Dispatcher.Invoke(() => SessionStatusText.Text = msg));

        try
        {
            // Network name: "RL-<yourCode>" so it's recognizable in ZeroTier Central.
            var networkId = await _zeroTier.CreateNetworkAndJoinAsync(
                $"RL-{_profile.MyFriendCode}", progress);

            _profile.LastNetworkId = networkId;
            _profile.Save();

            SessionNetworkIdText.Text = $"Network ID: {networkId}";
            SessionIpText.Text        = $"Your ZeroTier IP: {_zeroTier.LocalZeroTierIp}";
            SessionStatusText.Text    = "Ready — invite a friend below, then launch!";

            LaunchGameButton.IsEnabled   = true;
            LeaveNetworkButton.IsEnabled = true;

            // Start polling to authorize guests that join.
            StartGuestAuthorizationPolling(networkId);
        }
        catch (Exception ex)
        {
            SessionStatusText.Text = $"Error: {ex.Message}";
            Dispatcher.Invoke(() =>
            {
                HostJoinRow.Visibility  = Visibility.Visible;
                SessionPanel.Visibility = Visibility.Collapsed;
                HostGameButton.IsEnabled = true;
                JoinByIdButton.IsEnabled = true;
            });
        }
    }

    /// <summary>
    /// Every 5 seconds, auto-authorize any pending members the host's
    /// Central account sees on this network (i.e. when a guest joins).
    /// </summary>
    private void StartGuestAuthorizationPolling(string networkId)
    {
        if (_zeroTier is null) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) =>
        {
            if (_zeroTier.ActiveNetworkId != networkId)
            {
                timer.Stop();
                return;
            }

            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => SessionStatusText.Text = msg));

            try { await _zeroTier.AuthorizePendingMembersAsync(networkId, progress); }
            catch { /* non-fatal — keep polling */ }
        };
        timer.Start();
    }

    // ───────────────────────────────────────── Join by ID (manual fallback)
    private async void JoinByIdButton_Click(object sender, RoutedEventArgs e)
    {
        var networkId = InputDialog.Show(
            "Enter the ZeroTier Network ID shared by your friend:", "RocketLaunch — Join Game",
            owner: this);

        if (string.IsNullOrWhiteSpace(networkId)) return;

        await JoinNetworkAsync(networkId.Trim());
    }

    // ───────────────────────────────────────── Invite
    private async void InviteFriendButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string friendCode }) return;

        string sessionPayload;

        if (_zeroTier?.ActiveNetworkId is { } netId)
        {
            // Carry the real ZeroTier network ID in the invite payload.
            sessionPayload = netId;
        }
        else
        {
            MessageBox.Show(
                "Start a session first (click 'Host Game') before inviting friends.",
                "RocketLaunch");
            return;
        }

        var delivered = await _presence.SendInviteAsync(friendCode, _profile.MyDisplayName, sessionPayload);

        MessageBox.Show(
            delivered
                ? "Invite sent! Your friend will be connected automatically."
                : "That friend just went offline — couldn't deliver the invite.",
            "RocketLaunch");
    }

    // ───────────────────────────────────────── Receive invite
    private void OnInviteReceived(string fromDisplayName, string sessionPayload)
    {
        Dispatcher.Invoke(async () =>
        {
            var result = MessageBox.Show(
                $"{fromDisplayName} invited you to play!\n\nJoin their session and launch Rocket League?",
                "RocketLaunch — Invite",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            await JoinNetworkAsync(sessionPayload);
        });
    }

    // ───────────────────────────────────────── Join logic (shared)
    private async Task JoinNetworkAsync(string networkId)
    {
        if (_zeroTier is null)
        {
            MessageBox.Show(
                "Please configure your ZeroTier API token first.",
                "RocketLaunch");
            return;
        }

        HostGameButton.IsEnabled  = false;
        JoinByIdButton.IsEnabled  = false;
        HostJoinRow.Visibility    = Visibility.Collapsed;
        SessionPanel.Visibility   = Visibility.Visible;
        SessionTitleText.Text     = "Joining Session";
        SessionStatusText.Text    = "Joining network...";
        LaunchGameButton.IsEnabled   = false;
        LeaveNetworkButton.IsEnabled = false;

        var progress = new Progress<string>(msg => Dispatcher.Invoke(() => SessionStatusText.Text = msg));

        try
        {
            var ip = await _zeroTier.JoinNetworkAsync(networkId, progress);

            _profile.LastNetworkId = networkId;
            _profile.Save();

            SessionNetworkIdText.Text = $"Network ID: {networkId}";
            SessionIpText.Text        = $"Your ZeroTier IP: {ip}";
            SessionStatusText.Text    = "Connected! Launch Rocket League and join the LAN match.";

            LaunchGameButton.IsEnabled   = true;
            LeaveNetworkButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            SessionStatusText.Text = $"Error: {ex.Message}";
            HostJoinRow.Visibility  = Visibility.Visible;
            SessionPanel.Visibility = Visibility.Collapsed;
            HostGameButton.IsEnabled = true;
            JoinByIdButton.IsEnabled = true;
        }
    }

    // ───────────────────────────────────────── Launch game
    private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gameLauncher.Source == GameLauncherService.InstallSource.Unknown)
        {
            MessageBox.Show(
                "Rocket League installation not found.\n\n" +
                "Make sure the game is installed via Steam or Epic Games Launcher.",
                "RocketLaunch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SessionStatusText.Text = "Launching Rocket League...";
        LaunchGameButton.IsEnabled = false;

        try
        {
            _gameLauncher.Launch();
            SessionStatusText.Text = $"Rocket League launched via {_gameLauncher.Source}. " +
                                     "Go to Extras → LAN Play inside the game.";
        }
        catch (Exception ex)
        {
            SessionStatusText.Text    = $"Launch failed: {ex.Message}";
            LaunchGameButton.IsEnabled = true;
        }
    }

    // ───────────────────────────────────────── Leave session
    private async void LeaveNetworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_zeroTier?.ActiveNetworkId is { } netId)
        {
            try { await _zeroTier.LeaveNetworkAsync(netId); }
            catch { /* best-effort */ }
        }

        SessionPanel.Visibility   = Visibility.Collapsed;
        HostJoinRow.Visibility    = Visibility.Visible;
        HostGameButton.IsEnabled  = true;
        JoinByIdButton.IsEnabled  = true;
    }
}
