namespace BlueBattery;

/// <summary>Tray-Anwendung ohne Hauptfenster.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<ToolStripItem> _deviceItems = [];
    private readonly ToolStripMenuItem _forgetItem;
    private readonly Dictionary<Guid, DateTime> _lastNotified = [];
    private readonly DeviceCache _cache = DeviceCache.Load();

    private AppSettings _settings = AppSettings.Load();
    private IReadOnlyList<DeviceBattery> _devices = [];
    private SettingsForm? _settingsForm;
    private Icon? _currentIcon;
    private bool _refreshing;
    private bool _hasData;

    public TrayApplicationContext()
    {
        var settingsItem = new ToolStripMenuItem("Einstellungen …", null, (_, _) => ShowSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        var refreshItem = new ToolStripMenuItem("Jetzt aktualisieren", null, async (_, _) => await RefreshAsync());
        _autostartItem = new ToolStripMenuItem("Mit Windows starten", null, OnAutostartClicked);
        _forgetItem = new ToolStripMenuItem("Getrennte Geräte vergessen", null, (_, _) => ForgetDisconnected());
        var exitItem = new ToolStripMenuItem("Beenden", null, (_, _) => ExitThread());

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange([new ToolStripSeparator(), settingsItem, refreshItem, _forgetItem, _autostartItem, new ToolStripSeparator(), exitItem]);
        _menu.Opening += (_, _) =>
        {
            _autostartItem.Checked = Autostart.IsEnabled;
            _forgetItem.Enabled = _devices.Any(d => !d.IsConnected);
        };

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = "BlueBattery – lese Akkustände …",
            Visible = true,
        };
        SetIcon(null);

        // Linksklick öffnet ebenfalls das Menü.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowContextMenu();
        };

        _timer = new System.Windows.Forms.Timer();
        _timer.Tick += async (_, _) => await RefreshAsync();
        ApplyTimerInterval();
        _timer.Start();

        _ = RefreshAsync();
    }

    // ---- Daten ---------------------------------------------------------------

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            _devices = _cache.Merge(await BluetoothBatteryReader.ReadAsync());
            _hasData = true;
            UpdateUi();
            CheckNotifications();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateUi()
    {
        if (!_hasData)
            return;

        SetIcon(GetIconLevel());
        _notifyIcon.Text = BuildTooltip();
        RebuildDeviceItems();
    }

    /// <summary>Der Wert, den das Tray-Icon anzeigt – oder null, wenn es nichts anzuzeigen gibt.</summary>
    private int? GetIconLevel()
    {
        // Getrennte Geräte zeigen nur einen veralteten Wert und zählen daher nicht.
        var candidates = _devices.Where(d => d.IsConnected);

        if (_settings.IconSource == IconSource.SpecificDevice && _settings.IconDeviceId is Guid id)
            return candidates.FirstOrDefault(d => d.ContainerId == id)?.Level;

        var list = candidates.ToList();
        return list.Count > 0 ? list.Min(d => d.Level) : null;
    }

    private IEnumerable<DeviceBattery> VisibleDevices =>
        _settings.DisconnectedDisplay == DisconnectedDisplay.Hidden
            ? _devices.Where(d => d.IsConnected)
            : _devices;

    // ---- Menü / Tooltip ------------------------------------------------------

    private void RebuildDeviceItems()
    {
        foreach (var item in _deviceItems)
        {
            _menu.Items.Remove(item);
            item.Image?.Dispose();
            item.Dispose();
        }
        _deviceItems.Clear();

        var visible = VisibleDevices.ToList();
        if (visible.Count == 0)
        {
            _deviceItems.Add(new ToolStripMenuItem("Keine Bluetooth-Geräte mit Akkuanzeige") { Enabled = false });
        }
        else
        {
            foreach (var d in visible)
            {
                string text = d.IsConnected
                    ? $"{d.Name}: {d.Level} %"
                    : $"{d.Name}: {d.Level} % (getrennt{FormatLastSeen(d.LastSeen)})";
                _deviceItems.Add(new ToolStripMenuItem(text)
                {
                    Image = CreateSwatch(d.IsConnected ? d.Level : null),
                    ForeColor = d.IsConnected ? SystemColors.ControlText : SystemColors.GrayText,
                });
            }
        }

        for (int i = 0; i < _deviceItems.Count; i++)
            _menu.Items.Insert(i, _deviceItems[i]);
    }

    private static string FormatLastSeen(DateTime? lastSeen)
    {
        if (lastSeen is not DateTime t)
            return "";
        if (t.Date == DateTime.Today)
            return $", zuletzt {t:HH:mm}";
        if (t.Date == DateTime.Today.AddDays(-1))
            return $", zuletzt gestern {t:HH:mm}";
        return $", zuletzt {t:dd.MM. HH:mm}";
    }

    private void ForgetDisconnected()
    {
        _cache.ForgetDisconnected(_devices);
        _ = RefreshAsync();
    }

    private Bitmap CreateSwatch(int? level)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(TrayIconRenderer.ColorFor(level, _settings));
        g.FillEllipse(brush, 3, 3, 10, 10);
        return bmp;
    }

    private string BuildTooltip()
    {
        var connected = _devices.Where(d => d.IsConnected).ToList();
        if (connected.Count == 0)
            return "BlueBattery – kein verbundenes Gerät";

        // NotifyIcon.Text ist auf 127 Zeichen begrenzt.
        const int maxLength = 127;
        var lines = new List<string>();
        int length = 0;
        foreach (var d in connected)
        {
            string line = $"{d.Name}: {d.Level} %";
            int added = line.Length + (lines.Count > 0 ? 1 : 0);
            if (length + added > maxLength)
                break;
            lines.Add(line);
            length += added;
        }
        return string.Join("\n", lines);
    }

    // ---- Benachrichtigung ----------------------------------------------------

    private void CheckNotifications()
    {
        if (!_settings.NotifyEnabled)
        {
            _lastNotified.Clear();
            return;
        }

        var now = DateTime.Now;
        var repeatAfter = TimeSpan.FromMinutes(_settings.NotifyRepeatMinutes);
        var due = new List<DeviceBattery>();
        foreach (var d in _devices)
        {
            if (d.IsConnected && d.Level <= _settings.NotifyThreshold)
            {
                bool firstTime = !_lastNotified.TryGetValue(d.ContainerId, out var last);
                bool repeatDue = _settings.NotifyRepeat && now - last >= repeatAfter;
                if (firstTime || repeatDue)
                {
                    _lastNotified[d.ContainerId] = now;
                    due.Add(d);
                }
            }
            else
            {
                // Wieder geladen oder getrennt: beim nächsten Unterschreiten sofort erneut melden.
                _lastNotified.Remove(d.ContainerId);
            }
        }

        if (due.Count == 0)
            return;

        string text = string.Join("\n", due.Select(d => $"{d.Name}: {d.Level} %"));
        _notifyIcon.ShowBalloonTip(10_000, "Akkustand niedrig", text, ToolTipIcon.Warning);
    }

    // ---- Einstellungen -------------------------------------------------------

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _devices);
        _settingsForm.SettingsApplied += OnSettingsApplied;
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm?.Dispose();
            _settingsForm = null;
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OnSettingsApplied(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Einstellungen konnten nicht gespeichert werden:\n{ex.Message}", "BlueBattery",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        ApplyTimerInterval();
        UpdateUi();
        CheckNotifications();
    }

    private void ApplyTimerInterval() =>
        _timer.Interval = Math.Clamp(_settings.RefreshIntervalSeconds, 10, 3600) * 1000;

    // ---- Sonstiges -----------------------------------------------------------

    private void SetIcon(int? level)
    {
        var old = _currentIcon;
        _currentIcon = TrayIconRenderer.Create(level, _settings);
        _notifyIcon.Icon = _currentIcon;
        old?.Dispose();
    }

    private void ShowError(string message)
    {
        SetIcon(null);
        string text = $"BlueBattery – Fehler: {message}";
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void ShowContextMenu()
    {
        // Öffnet das Menü wie bei einem Rechtsklick (inkl. korrekter Positionierung und Schließen).
        var method = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method?.Invoke(_notifyIcon, null);
    }

    private void OnAutostartClicked(object? sender, EventArgs e)
    {
        try
        {
            Autostart.Set(!Autostart.IsEnabled);
            _autostartItem.Checked = Autostart.IsEnabled;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Autostart konnte nicht geändert werden:\n{ex.Message}", "BlueBattery",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _settingsForm?.Close();
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _currentIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
