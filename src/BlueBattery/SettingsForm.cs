namespace BlueBattery;

/// <summary>Einstellungsfenster: Benachrichtigung, Taskleistensymbol, getrennte Geräte, Allgemein.</summary>
internal sealed class SettingsForm : Form
{
    private const int PreviewSize = 28;

    private readonly AppSettings _settings;

    private readonly CheckBox _notifyEnabled = new() { Text = "Akkustand-Meldung bei", AutoSize = true };
    private readonly NumericUpDown _notifyThreshold = Percent();
    private readonly CheckBox _notifyRepeat = new() { Text = "Meldung wiederholen alle", AutoSize = true };
    private readonly NumericUpDown _notifyRepeatMinutes = new()
    {
        Minimum = 1,
        Maximum = 1440,
        Width = 60,
        TextAlign = HorizontalAlignment.Right,
    };

    private readonly CheckBox _showPercent = new() { Text = "Akkustandsangabe im Symbol anzeigen", AutoSize = true };
    private readonly NumericUpDown _critical = Percent();
    private readonly NumericUpDown _low = Percent();
    private readonly Button _criticalColor = ColorButton("Farbe für kritischen Akkustand");
    private readonly Button _lowColor = ColorButton("Farbe für niedrigen Akkustand");
    private readonly Button _normalColor = ColorButton("Farbe für normalen Akkustand");

    private readonly RadioButton _iconLowest = new() { Text = "Der niedrigsten Batterie", AutoSize = true };
    private readonly RadioButton _iconDevice = new() { Text = "Eines bestimmten Geräts:", AutoSize = true };
    private readonly ComboBox _iconDeviceList = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

    private readonly RadioButton _discGrayed = new() { Text = "In Graustufen anzeigen", AutoSize = true };
    private readonly RadioButton _discHidden = new() { Text = "Ausblenden", AutoSize = true };

    private readonly NumericUpDown _interval = new() { Minimum = 10, Maximum = 3600, Increment = 10, Width = 70 };
    private readonly CheckBox _autostart = new() { Text = "Mit Windows starten", AutoSize = true };

    /// <summary>Wird ausgelöst, wenn der Benutzer mit OK oder Übernehmen speichert.</summary>
    public event Action<AppSettings>? SettingsApplied;

    public SettingsForm(AppSettings current, IReadOnlyList<DeviceBattery> devices)
    {
        _settings = current.Clone();

        Text = "BlueBattery – Einstellungen";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Icon = TrayIconRenderer.Create(100, _settings);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        root.Controls.Add(BuildIconGroup(devices));
        root.Controls.Add(BuildDisconnectedGroup());
        root.Controls.Add(BuildGeneralGroup());
        root.Controls.Add(BuildButtons());
        Controls.Add(root);

        LoadValues();
        WireEvents();
        UpdatePreviews();
        UpdateEnabledState();
    }

    // ---- Aufbau --------------------------------------------------------------

    private GroupBox BuildIconGroup(IReadOnlyList<DeviceBattery> devices)
    {
        var table = Table();

        table.Controls.Add(Row(_notifyEnabled, _notifyThreshold, PercentLabel()));
        var repeatRow = Row(_notifyRepeat, _notifyRepeatMinutes, new Label { Text = "Minuten", AutoSize = true });
        repeatRow.Margin = new Padding(18, 0, 0, 0); // eingerückt: gehört zur Akkustand-Meldung
        table.Controls.Add(repeatRow);
        table.Controls.Add(_showPercent);

        var thresholds = Row(
            _criticalColor, Stack(new Label { Text = "Kritischer Grenzwert", AutoSize = true }, Row(_critical, PercentLabel())),
            _lowColor, Stack(new Label { Text = "Niedriggrenzwert", AutoSize = true }, Row(_low, PercentLabel())),
            _normalColor);
        thresholds.Margin = new Padding(0, 6, 0, 6);
        table.Controls.Add(thresholds);

        table.Controls.Add(new Label
        {
            Text = "Taskleistensymbol zeigt folgenden Akkustand an:",
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3),
        });

        // Beide Radiobuttons im selben Container, damit sie eine Gruppe bilden.
        var sourcePanel = Table();
        sourcePanel.Controls.Add(_iconLowest);
        sourcePanel.Controls.Add(Row(_iconDevice, _iconDeviceList));
        table.Controls.Add(sourcePanel);

        foreach (var d in devices)
            _iconDeviceList.Items.Add(new DeviceItem(d.ContainerId, d.Name));
        if (_settings.IconDeviceId is Guid id && devices.All(d => d.ContainerId != id))
            _iconDeviceList.Items.Add(new DeviceItem(id, $"{_settings.IconDeviceName ?? "Unbekannt"} (nicht verfügbar)"));

        return Group("Benachrichtigung / Taskleistensymbol", table);
    }

    private GroupBox BuildDisconnectedGroup()
    {
        var table = Table();
        table.Controls.Add(Row(new Label { Text = "Im Tray-Menü:", AutoSize = true, Anchor = AnchorStyles.Left }, _discGrayed, _discHidden));
        table.Controls.Add(new Label
        {
            Text = "Angezeigt wird der zuletzt bekannte Akkustand. Für das Taskleistensymbol\nzählen nur verbundene Geräte.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 3, 3, 3),
        });
        return Group("Getrennte Geräte", table);
    }

    private GroupBox BuildGeneralGroup()
    {
        var table = Table();
        table.Controls.Add(Row(
            new Label { Text = "Aktualisieren alle", AutoSize = true, Anchor = AnchorStyles.Left },
            _interval,
            new Label { Text = "Sekunden", AutoSize = true, Anchor = AnchorStyles.Left }));
        table.Controls.Add(_autostart);
        return Group("Allgemein", table);
    }

    private FlowLayoutPanel BuildButtons()
    {
        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Abbrechen", AutoSize = true, MinimumSize = new Size(80, 0) };
        var apply = new Button { Text = "Übernehmen", AutoSize = true, MinimumSize = new Size(80, 0) };

        // Das Fenster ist nicht modal; DialogResult allein würde es nicht schließen.
        ok.Click += (_, _) => { if (Apply()) Close(); };
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => Apply();

        AcceptButton = ok;
        CancelButton = cancel;

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
        };
        panel.Controls.AddRange([apply, cancel, ok]);
        return panel;
    }

    // ---- Werte ---------------------------------------------------------------

    private void LoadValues()
    {
        _notifyEnabled.Checked = _settings.NotifyEnabled;
        _notifyThreshold.Value = _settings.NotifyThreshold;
        _notifyRepeat.Checked = _settings.NotifyRepeat;
        _notifyRepeatMinutes.Value = Math.Clamp(_settings.NotifyRepeatMinutes, 1, 1440);
        _showPercent.Checked = _settings.ShowPercentOnIcon;
        _critical.Value = _settings.CriticalThreshold;
        _low.Value = _settings.LowThreshold;

        _iconLowest.Checked = _settings.IconSource == IconSource.Lowest;
        _iconDevice.Checked = _settings.IconSource == IconSource.SpecificDevice;
        _iconDeviceList.SelectedItem = _iconDeviceList.Items.Cast<DeviceItem>()
            .FirstOrDefault(i => i.Id == _settings.IconDeviceId);
        if (_iconDeviceList.SelectedIndex < 0 && _iconDeviceList.Items.Count > 0)
            _iconDeviceList.SelectedIndex = 0;

        _discGrayed.Checked = _settings.DisconnectedDisplay == DisconnectedDisplay.Grayed;
        _discHidden.Checked = _settings.DisconnectedDisplay == DisconnectedDisplay.Hidden;

        _interval.Value = Math.Clamp(_settings.RefreshIntervalSeconds, (int)_interval.Minimum, (int)_interval.Maximum);
        _autostart.Checked = Autostart.IsEnabled;
    }

    private void WireEvents()
    {
        _notifyEnabled.CheckedChanged += (_, _) => UpdateEnabledState();
        _notifyRepeat.CheckedChanged += (_, _) => UpdateEnabledState();
        // Die beiden Radiobuttons liegen in unterschiedlichen Containern (der zweite steht
        // zusammen mit der Geräteliste in einer Zeile), daher gegenseitig selbst abwählen.
        _iconLowest.CheckedChanged += (_, _) =>
        {
            if (_iconLowest.Checked)
                _iconDevice.Checked = false;
        };
        _iconDevice.CheckedChanged += (_, _) =>
        {
            if (_iconDevice.Checked)
                _iconLowest.Checked = false;
            UpdateEnabledState();
        };
        _showPercent.CheckedChanged += (_, _) => { ReadPreviewValues(); UpdatePreviews(); };

        _critical.ValueChanged += (_, _) =>
        {
            if (_low.Value <= _critical.Value)
                _low.Value = Math.Min(_low.Maximum, _critical.Value + 1);
            ReadPreviewValues();
            UpdatePreviews();
        };
        _low.ValueChanged += (_, _) =>
        {
            if (_critical.Value >= _low.Value)
                _critical.Value = Math.Max(_critical.Minimum, _low.Value - 1);
            ReadPreviewValues();
            UpdatePreviews();
        };

        _criticalColor.Click += (_, _) => PickColor(c => _settings.CriticalColor = c, _settings.CriticalColorValue);
        _lowColor.Click += (_, _) => PickColor(c => _settings.LowColor = c, _settings.LowColorValue);
        _normalColor.Click += (_, _) => PickColor(c => _settings.NormalColor = c, _settings.NormalColorValue);
    }

    private void ReadPreviewValues()
    {
        _settings.ShowPercentOnIcon = _showPercent.Checked;
        _settings.CriticalThreshold = (int)_critical.Value;
        _settings.LowThreshold = (int)_low.Value;
    }

    private void PickColor(Action<string> set, Color current)
    {
        using var dialog = new ColorDialog { Color = current, FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        set(AppSettings.ToHex(dialog.Color));
        UpdatePreviews();
    }

    private void UpdatePreviews()
    {
        SetPreview(_criticalColor, _settings.CriticalThreshold);
        SetPreview(_lowColor, _settings.LowThreshold);
        SetPreview(_normalColor, 100);
    }

    private void SetPreview(Button button, int level)
    {
        var old = button.Image;
        int size = LogicalToDeviceUnits(PreviewSize);
        button.Image = TrayIconRenderer.CreateBitmap(level, _settings, size);
        button.Size = new Size(size + LogicalToDeviceUnits(12), size + LogicalToDeviceUnits(12));
        // Bei ausgeblendeter Prozentangabe ist das Symbol hell bzw. dunkel – Hintergrund passend wählen.
        button.BackColor = _settings.ShowPercentOnIcon ? SystemColors.Control : Color.FromArgb(40, 40, 40);
        old?.Dispose();
    }

    private void UpdateEnabledState()
    {
        _notifyThreshold.Enabled = _notifyEnabled.Checked;
        _notifyRepeat.Enabled = _notifyEnabled.Checked;
        _notifyRepeatMinutes.Enabled = _notifyEnabled.Checked && _notifyRepeat.Checked;
        _iconDeviceList.Enabled = _iconDevice.Checked;
    }

    private bool Apply()
    {
        if (_iconDevice.Checked && _iconDeviceList.SelectedItem is null)
        {
            MessageBox.Show(this, "Bitte ein Gerät für das Taskleistensymbol auswählen.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        _settings.NotifyEnabled = _notifyEnabled.Checked;
        _settings.NotifyThreshold = (int)_notifyThreshold.Value;
        _settings.NotifyRepeat = _notifyRepeat.Checked;
        _settings.NotifyRepeatMinutes = (int)_notifyRepeatMinutes.Value;
        ReadPreviewValues();

        _settings.IconSource = _iconDevice.Checked ? IconSource.SpecificDevice : IconSource.Lowest;
        if (_iconDeviceList.SelectedItem is DeviceItem item)
        {
            _settings.IconDeviceId = item.Id;
            _settings.IconDeviceName = item.Name.Replace(" (nicht verfügbar)", "");
        }

        _settings.DisconnectedDisplay = _discHidden.Checked ? DisconnectedDisplay.Hidden : DisconnectedDisplay.Grayed;
        _settings.RefreshIntervalSeconds = (int)_interval.Value;

        try
        {
            if (_autostart.Checked != Autostart.IsEnabled)
                Autostart.Set(_autostart.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Autostart konnte nicht geändert werden:\n{ex.Message}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        SettingsApplied?.Invoke(_settings.Clone());
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _criticalColor.Image?.Dispose();
            _lowColor.Image?.Dispose();
            _normalColor.Image?.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---- Hilfen --------------------------------------------------------------

    private sealed record DeviceItem(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static NumericUpDown Percent() => new()
    {
        Minimum = 0,
        Maximum = 100,
        Width = 60,
        TextAlign = HorizontalAlignment.Right,
    };

    private static Label PercentLabel() => new() { Text = "%", AutoSize = true };

    private static Button ColorButton(string tooltip)
    {
        var b = new Button
        {
            FlatStyle = FlatStyle.Flat,
            ImageAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Left,
            AccessibleName = tooltip,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = SystemColors.ControlDark;
        new ToolTip().SetToolTip(b, tooltip + " – klicken zum Ändern");
        return b;
    }

    private static TableLayoutPanel Table() => new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };
        foreach (var c in controls)
        {
            if (c is Label or CheckBox or RadioButton)
                c.Margin = new Padding(3, 6, 3, 3);
            panel.Controls.Add(c);
        }
        return panel;
    }

    private static TableLayoutPanel Stack(params Control[] controls)
    {
        var t = Table();
        t.Dock = DockStyle.None;
        t.Anchor = AnchorStyles.Left;
        t.Margin = new Padding(6, 0, 6, 0);
        foreach (var c in controls)
            t.Controls.Add(c);
        return t;
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(0, 0, 0, 8),
        };
        group.Controls.Add(content);
        return group;
    }
}
