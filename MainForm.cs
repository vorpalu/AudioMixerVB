using System.Diagnostics;
using System.Globalization;

namespace AudioMixerVB;

public partial class MainForm : Form
{
    private static readonly string[] StreamChannelNames = ["Game", "Chat", "Music", "Media"];

    private readonly AudioEndpointController audioEndpointController = new();
    private readonly AudioSessionController audioSessionController = new();
    private readonly MonitorMixEngine monitorMixEngine = new();
    private readonly StreamMixEngine streamMixEngine = new();
    private readonly UndocumentedAudioPolicyRouter audioPolicyRouter = new();
    private readonly SettingsService settingsService = new();
    private readonly SerialController serialController = new();
    private readonly Dictionary<string, ChannelControls> channelControlsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> lastAutoRoutingMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AutoRoutingAttempt> lastAutoRoutingAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> channelAppChipSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan autoRefreshInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan autoRoutingCooldown = TimeSpan.FromSeconds(15);
    private readonly List<MixerChannel> channels;

    private AppSettings settings;
    private IReadOnlyList<AudioEndpoint> endpoints = [];
    private IReadOnlyList<AudioEndpoint> captureEndpoints = [];
    private IReadOnlyList<AudioAppSession> appSessions = [];
    private IReadOnlyList<AudioAppGroup> appGroups = [];
    private bool updatingAppRoutingUi;
    private bool updatingMonitorUi;
    private bool updatingStreamUi;
    private bool updatingSerialUi;
    private bool monitorOperationInProgress;
    private bool streamOperationInProgress;
    private bool isRefreshingApps;
    private bool isApplyingRouting;
    private bool mixerDropTargetsRegistered;
    private bool allowCloseAfterAudioStop;
    private DateTime lastRefreshAppsUtc = DateTime.MinValue;
    private string unassignedAppChipSignature = "\u0000";

    private GroupBox monitorMixGroupBox = null!;
    private TableLayoutPanel monitorMixLayout = null!;
    private ComboBox monitorOutputComboBox = null!;
    private ComboBox monitorGameInputComboBox = null!;
    private ComboBox monitorChatInputComboBox = null!;
    private ComboBox monitorMusicInputComboBox = null!;
    private ComboBox monitorMediaInputComboBox = null!;
    private ComboBox channelSliderModeComboBox = null!;
    private Button startMonitorButton = null!;
    private Button stopMonitorButton = null!;
    private Button restartMonitorButton = null!;
    private Label monitorStatusValueLabel = null!;
    private NumericUpDown monitorLatencyNumericUpDown = null!;
    private CheckBox monitorExclusiveCheckBox = null!;
    private Label monitorWarningLabel = null!;
    private GroupBox streamMixGroupBox = null!;
    private TableLayoutPanel streamMixLayout = null!;
    private ComboBox streamOutputComboBox = null!;
    private Button startStreamButton = null!;
    private Button stopStreamButton = null!;
    private Button restartStreamButton = null!;
    private Label streamStatusValueLabel = null!;
    private Label streamLatencyValueLabel = null!;
    private NumericUpDown streamLatencyNumericUpDown = null!;
    private Label streamWarningLabel = null!;
    private DualMixChannelStripControl masterStripControl = null!;
    private Panel unassignedAppsContainerPanel = null!;
    private FlowLayoutPanel unassignedAppsFlowPanel = null!;
    private Label unassignedAppsTitleLabel = null!;
    private Button clearLogButton = null!;
    private Button copyLogButton = null!;
    private CheckBox autoScrollLogCheckBox = null!;

    public MainForm()
    {
        InitializeComponent();
        audioEndpointController.LogMessage += (_, message) => RunOnUiThread(() => AppendLog(message));
        audioSessionController.LogMessage += (_, message) => RunOnUiThread(() => AppendLog(message));
        monitorMixEngine.OnLog += (_, message) => RunOnUiThread(() => AppendLog(message));
        monitorMixEngine.OnError += (_, ex) => RunOnUiThread(() => AppendLog($"Monitor error: {ex.Message}"));
        streamMixEngine.OnLog += (_, message) => RunOnUiThread(() => AppendLog(message));
        streamMixEngine.OnError += (_, ex) => RunOnUiThread(() => AppendLog($"Stream error: {ex.Message}"));

        settings = settingsService.Load(out var settingsFileLoaded, out var settingsError);
        channels = settings.Channels;

        BuildMonitorMixPanel();
        BuildStreamMixPanel();
        SetupAppSessionsGrid();
        LoadRoutingOptions();
        BuildMixerUnassignedAppsPanel();
        BuildChannelPanels();
        BuildLogTools();
        SetMonitorButtons(isRunning: false, operationInProgress: false);
        SetStreamButtons(isRunning: false, operationInProgress: false);
        ApplyDarkTheme();
        WireEvents();
        LoadSerialSettings();
        RefreshComPorts();
        RefreshEndpoints(applyFirstRunAutoMapping: !settingsFileLoaded);
        Shown += async (_, _) =>
        {
            RegisterMixerDropTargets();
            await RefreshAppSessionsAsync(logDetails: true, force: true, bindGrid: true);
        };
        if (settings.MonitorMix.EnabledOnStartup)
        {
            StartMonitor();
        }

        if (settings.StreamMix.EnabledOnStartup)
        {
            StartStreamMix();
        }

        AppendLog($"Settings path: {settingsService.SettingsPath}");
        if (settingsFileLoaded)
        {
            AppendLog("Settings loaded.");
        }
        else
        {
            AppendLog("No settings file found; using defaults.");
        }

        if (!string.IsNullOrWhiteSpace(settingsError))
        {
            AppendLog($"Settings load error: {settingsError}");
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowCloseAfterAudioStop && ShouldStopAudioBeforeClose())
        {
            e.Cancel = true;
            Enabled = false;
            AppendLog("Stopping audio engines before close.");

            await StopAudioBeforeCloseAsync();

            allowCloseAfterAudioStop = true;
            RunOnUiThread(Close);
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        monitorMixEngine.Dispose();
        streamMixEngine.Dispose();
        serialController.Dispose();
        base.OnFormClosed(e);
    }

    private bool ShouldStopAudioBeforeClose()
        => monitorMixEngine.IsRunning ||
           monitorMixEngine.IsStopping ||
           streamMixEngine.IsRunning ||
           streamMixEngine.IsStopping;

    private async Task StopAudioBeforeCloseAsync()
    {
        try
        {
            var stopTask = Task.WhenAll(monitorMixEngine.StopAsync(), streamMixEngine.StopAsync());
            var completedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completedTask != stopTask)
            {
                AppendLog("Audio stop timed out during close; closing anyway.");
                return;
            }

            await stopTask;
        }
        catch (Exception ex)
        {
            AppendLog($"Audio stop during close error: {ex.Message}");
        }
    }

    private void WireEvents()
    {
        refreshEndpointsButton.Click += (_, _) => RefreshEndpoints(applyFirstRunAutoMapping: false);
        monitorOutputComboBox.SelectedIndexChanged += (_, _) => HandleMonitorOutputChanged();
        monitorGameInputComboBox.SelectedIndexChanged += (_, _) => HandleMonitorInputChanged("Game", monitorGameInputComboBox);
        monitorChatInputComboBox.SelectedIndexChanged += (_, _) => HandleMonitorInputChanged("Chat", monitorChatInputComboBox);
        monitorMusicInputComboBox.SelectedIndexChanged += (_, _) => HandleMonitorInputChanged("Music", monitorMusicInputComboBox);
        monitorMediaInputComboBox.SelectedIndexChanged += (_, _) => HandleMonitorInputChanged("Media", monitorMediaInputComboBox);
        channelSliderModeComboBox.SelectedIndexChanged += (_, _) => HandleChannelSliderModeChanged();
        masterStripControl.MonitorTrackBar.ValueChanged += (_, _) => HandleMonitorMasterGainChanged();
        masterStripControl.MonitorMuteButton.Click += (_, _) => ToggleMonitorMasterMute();
        masterStripControl.StreamTrackBar.ValueChanged += (_, _) => HandleMixerStreamMasterGainChanged();
        masterStripControl.StreamMuteButton.Click += (_, _) => ToggleMixerStreamMasterMute();
        monitorLatencyNumericUpDown.ValueChanged += (_, _) => HandleMonitorLatencyChanged();
        monitorExclusiveCheckBox.CheckedChanged += (_, _) => HandleMonitorExclusiveChanged();
        startMonitorButton.Click += (_, _) => StartMonitor();
        stopMonitorButton.Click += async (_, _) => await StopMonitorAsync();
        restartMonitorButton.Click += async (_, _) => await RestartMonitorAsync();
        streamOutputComboBox.SelectedIndexChanged += (_, _) => HandleStreamOutputChanged();
        streamLatencyNumericUpDown.ValueChanged += (_, _) => HandleStreamLatencyChanged();
        startStreamButton.Click += (_, _) => StartStreamMix();
        stopStreamButton.Click += async (_, _) => await StopStreamMixAsync();
        restartStreamButton.Click += async (_, _) => await RestartStreamMixAsync();
        refreshAppsButton.Click += async (_, _) => await RefreshAppSessionsAsync(logDetails: true, force: true, bindGrid: true);
        applyRoutingButton.Click += (_, _) => ApplyRouting(autoTriggered: false);
        saveRoutingRulesButton.Click += (_, _) => SaveRoutingRulesFromGrid(persist: true);
        clearRoutingButton.Click += (_, _) => ClearRoutingRules();
        openWindowsVolumeMixerButton.Click += (_, _) => OpenWindowsVolumeMixer();
        enableExperimentalRoutingCheckBox.CheckedChanged += (_, _) =>
        {
            settings.EnableExperimentalAutomaticRouting = enableExperimentalRoutingCheckBox.Checked;
            SaveSettings();
        };
        autoApplyRoutingRulesCheckBox.CheckedChanged += (_, _) =>
        {
            settings.AutoApplyRoutingRules = autoApplyRoutingRulesCheckBox.Checked;
            autoApplyRoutingTimer.Enabled = settings.AutoApplyRoutingRules;
            SaveSettings();
        };
        autoApplyRoutingTimer.Tick += async (_, _) => await HandleAutoApplyTimerTickAsync();
        meterUpdateTimer.Tick += (_, _) => UpdateMeters();
        meterUpdateTimer.Start();
        showRawSessionsCheckBox.CheckedChanged += (_, _) => BindAppSessionsGrid();
        appSessionsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (appSessionsGrid.IsCurrentCellDirty)
            {
                appSessionsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        appSessionsGrid.CellValueChanged += (_, args) =>
        {
            if (updatingAppRoutingUi)
            {
                return;
            }

            if (args.RowIndex >= 0 && appSessionsGrid.Columns[args.ColumnIndex].Name == "AssignedChannel")
            {
                SaveRoutingRulesFromGrid(persist: false);
                AssignChannelsToSessions();
                appGroups = BuildAppGroups();
                BindAppSessionsGrid();
                UpdateChannelControlAvailability();
                if (settings.AutoApplyRoutingRules)
                {
                    _ = AutoApplyPendingRoutesAsync();
                }
            }
        };
        appSessionsGrid.DataError += (_, args) =>
        {
            args.ThrowException = false;
            AppendLog($"Application routing grid error: {args.Exception?.Message}");
        };
        refreshComButton.Click += (_, _) => RefreshComPorts();
        connectSerialButton.Click += (_, _) => ToggleSerialConnection();
        serialPortComboBox.SelectedIndexChanged += (_, _) => SaveSerialSelection();
        baudRateTextBox.TextChanged += (_, _) => SaveBaudRateIfValid();
        clearLogButton.Click += (_, _) => logTextBox.Clear();
        copyLogButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(logTextBox.Text))
            {
                Clipboard.SetText(logTextBox.Text);
            }
        };
        serialController.OnCommandReceived += (_, args) => RunOnUiThread(() => HandleSerialCommand(args));
        serialController.OnLogMessage += (_, message) => RunOnUiThread(() => AppendLog(message));
    }

    private void BuildMonitorMixPanel()
    {
        monitorTabPage.SuspendLayout();

        monitorMixGroupBox = new GroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(10),
            Text = "Monitor Mix"
        };

        monitorMixLayout = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            RowCount = 8
        };
        monitorMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        monitorMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        monitorMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        monitorMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        for (var row = 0; row < 6; row++)
        {
            monitorMixLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        }
        monitorMixLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        monitorMixLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        monitorOutputComboBox = CreateMonitorComboBox();
        monitorGameInputComboBox = CreateMonitorComboBox();
        monitorChatInputComboBox = CreateMonitorComboBox();
        monitorMusicInputComboBox = CreateMonitorComboBox();
        monitorMediaInputComboBox = CreateMonitorComboBox();
        channelSliderModeComboBox = CreateMonitorComboBox();
        channelSliderModeComboBox.Items.AddRange(["Monitor Mix Gain"]);

        startMonitorButton = new Button { Dock = DockStyle.Fill, Text = "Start Monitor", UseVisualStyleBackColor = true };
        stopMonitorButton = new Button { Dock = DockStyle.Fill, Text = "Stop", UseVisualStyleBackColor = true };
        restartMonitorButton = new Button { Dock = DockStyle.Fill, Text = "Restart", UseVisualStyleBackColor = true };
        monitorStatusValueLabel = new Label { Dock = DockStyle.Fill, Text = "Stopped", TextAlign = ContentAlignment.MiddleLeft };
        monitorLatencyNumericUpDown = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 10,
            Maximum = 500,
            Increment = 5,
            Value = Math.Clamp(settings.MonitorMix.LatencyMs, 10, 500)
        };
        monitorWarningLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.DarkOrange,
            TextAlign = ContentAlignment.MiddleLeft
        };

        AddMonitorRow(0, "Output", monitorOutputComboBox, "Mode", channelSliderModeComboBox);
        AddMonitorRow(1, "Game", monitorGameInputComboBox, "Chat", monitorChatInputComboBox);
        AddMonitorRow(2, "Music", monitorMusicInputComboBox, "Media", monitorMediaInputComboBox);
        AddMonitorRow(3, "Status", monitorStatusValueLabel, "Latency", monitorLatencyNumericUpDown);

        monitorExclusiveCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Exclusive output (lowest latency, locks the device)",
            Checked = settings.MonitorMix.ExclusiveOutput
        };
        monitorMixLayout.Controls.Add(monitorExclusiveCheckBox, 1, 4);
        monitorMixLayout.SetColumnSpan(monitorExclusiveCheckBox, 3);

        monitorMixLayout.Controls.Add(startMonitorButton, 0, 6);
        monitorMixLayout.SetColumnSpan(startMonitorButton, 2);
        monitorMixLayout.Controls.Add(stopMonitorButton, 2, 6);
        monitorMixLayout.Controls.Add(restartMonitorButton, 3, 6);
        monitorMixLayout.Controls.Add(monitorWarningLabel, 0, 7);
        monitorMixLayout.SetColumnSpan(monitorWarningLabel, 4);

        monitorMixGroupBox.Controls.Add(monitorMixLayout);
        monitorTabPage.Controls.Add(monitorMixGroupBox);
        monitorTabPage.ResumeLayout();
    }

    private void BuildStreamMixPanel()
    {
        streamTabPage.SuspendLayout();

        streamMixGroupBox = new GroupBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(10),
            Text = "Stream Mix for OBS"
        };

        var rootLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        streamMixLayout = new TableLayoutPanel
        {
            ColumnCount = 6,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        streamMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        streamMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        streamMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        streamMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        streamMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        streamMixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        for (var row = 0; row < 3; row++)
        {
            streamMixLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        }

        streamOutputComboBox = CreateMonitorComboBox();
        startStreamButton = new Button { Dock = DockStyle.Fill, Text = "Start Stream Mix", UseVisualStyleBackColor = true };
        stopStreamButton = new Button { Dock = DockStyle.Fill, Text = "Stop", UseVisualStyleBackColor = true };
        restartStreamButton = new Button { Dock = DockStyle.Fill, Text = "Restart", UseVisualStyleBackColor = true };
        streamStatusValueLabel = new Label { Dock = DockStyle.Fill, Text = "Stopped", TextAlign = ContentAlignment.MiddleLeft };
        streamLatencyValueLabel = new Label { Dock = DockStyle.Fill, Text = $"{settings.StreamMix.LatencyMs} ms", TextAlign = ContentAlignment.MiddleLeft };
        streamLatencyNumericUpDown = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 10,
            Maximum = 500,
            Increment = 5,
            Value = Math.Clamp(settings.StreamMix.LatencyMs, 10, 500)
        };
        streamWarningLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.DarkOrange,
            TextAlign = ContentAlignment.MiddleLeft
        };

        AddStreamRow(0, "Output", streamOutputComboBox, "Status", streamStatusValueLabel);
        AddStreamRow(1, "Latency", streamLatencyNumericUpDown, "Value", streamLatencyValueLabel);
        streamMixLayout.Controls.Add(startStreamButton, 0, 2);
        streamMixLayout.SetColumnSpan(startStreamButton, 2);
        streamMixLayout.Controls.Add(stopStreamButton, 2, 2);
        streamMixLayout.Controls.Add(restartStreamButton, 3, 2);

        rootLayout.Controls.Add(streamMixLayout, 0, 0);
        rootLayout.Controls.Add(streamWarningLabel, 0, 1);
        streamMixGroupBox.Controls.Add(rootLayout);
        streamTabPage.Controls.Add(streamMixGroupBox);
        streamTabPage.ResumeLayout();
    }

    private void AddStreamRow(
        int row,
        string leftLabel,
        Control leftControl,
        string rightLabel,
        Control rightControl)
    {
        streamMixLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = leftLabel,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        streamMixLayout.Controls.Add(leftControl, 1, row);
        streamMixLayout.SetColumnSpan(leftControl, 3);
        streamMixLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = rightLabel,
            TextAlign = ContentAlignment.MiddleLeft
        }, 4, row);
        streamMixLayout.Controls.Add(rightControl, 5, row);
    }

    private static ComboBox CreateMonitorComboBox()
    {
        return new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
    }

    private void AddMonitorRow(
        int row,
        string leftLabel,
        Control leftControl,
        string rightLabel,
        Control rightControl)
    {
        monitorMixLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = leftLabel,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        monitorMixLayout.Controls.Add(leftControl, 1, row);
        monitorMixLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = rightLabel,
            TextAlign = ContentAlignment.MiddleLeft
        }, 2, row);
        monitorMixLayout.Controls.Add(rightControl, 3, row);
    }

    private void ApplyDarkTheme()
    {
        ApplyDarkTheme(this);

        appSessionsGrid.BackgroundColor = Color.FromArgb(24, 26, 31);
        appSessionsGrid.GridColor = Color.FromArgb(55, 60, 70);
        appSessionsGrid.EnableHeadersVisualStyles = false;
        appSessionsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 42, 50);
        appSessionsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;
        appSessionsGrid.DefaultCellStyle.BackColor = Color.FromArgb(28, 31, 36);
        appSessionsGrid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        appSessionsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 89, 130);
        appSessionsGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        appSessionsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 36, 42);
        appSessionsGrid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 42, 50);
        appSessionsGrid.RowHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;
    }

    private void BuildLogTools()
    {
        var existingLogTextBox = logTextBox;
        logGroupBox.Controls.Clear();

        var logLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        clearLogButton = new Button
        {
            AutoSize = true,
            Text = "Clear log",
            UseVisualStyleBackColor = true
        };
        copyLogButton = new Button
        {
            AutoSize = true,
            Text = "Copy log",
            UseVisualStyleBackColor = true
        };
        autoScrollLogCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = true,
            Text = "Auto-scroll",
            UseVisualStyleBackColor = true
        };

        toolbar.Controls.Add(clearLogButton);
        toolbar.Controls.Add(copyLogButton);
        toolbar.Controls.Add(autoScrollLogCheckBox);
        logLayout.Controls.Add(toolbar, 0, 0);
        logLayout.Controls.Add(existingLogTextBox, 0, 1);
        logGroupBox.Controls.Add(logLayout);
    }

    private static void ApplyDarkTheme(Control control)
    {
        if (control is ChannelStripControl or DualMixChannelStripControl or AppChipControl)
        {
            return;
        }

        switch (control)
        {
            case Form:
            case TabPage:
            case TableLayoutPanel:
            case FlowLayoutPanel:
            case Panel:
                control.BackColor = Color.FromArgb(24, 26, 31);
                control.ForeColor = Color.Gainsboro;
                break;
            case GroupBox:
                control.BackColor = Color.FromArgb(28, 31, 36);
                control.ForeColor = Color.WhiteSmoke;
                break;
            case Label:
                control.BackColor = Color.Transparent;
                control.ForeColor = Color.Gainsboro;
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.BackColor = Color.FromArgb(48, 52, 60);
                button.ForeColor = Color.WhiteSmoke;
                button.FlatStyle = FlatStyle.Flat;
                break;
            case TextBox textBox:
                textBox.BackColor = Color.FromArgb(19, 21, 25);
                textBox.ForeColor = Color.Gainsboro;
                break;
            case NumericUpDown numericUpDown:
                numericUpDown.BackColor = Color.FromArgb(38, 42, 50);
                numericUpDown.ForeColor = Color.WhiteSmoke;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = Color.FromArgb(38, 42, 50);
                comboBox.ForeColor = Color.WhiteSmoke;
                break;
            case CheckBox checkBox:
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = Color.Gainsboro;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyDarkTheme(child);
        }
    }

    private void BuildMixerUnassignedAppsPanel()
    {
        channelsContainer.SuspendLayout();
        channelsContainer.Controls.Remove(channelsTable);
        channelsContainer.RowStyles.Clear();
        channelsContainer.RowCount = 3;
        channelsContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        channelsContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        channelsContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        unassignedAppsContainerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(8),
            BackColor = Color.FromArgb(32, 36, 43)
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 1,
            BackColor = Color.FromArgb(32, 36, 43)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        unassignedAppsTitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(242, 244, 248),
            Text = "Unassigned Apps",
            TextAlign = ContentAlignment.MiddleLeft
        };

        unassignedAppsFlowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 27, 33),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(4)
        };

        layout.Controls.Add(unassignedAppsTitleLabel, 0, 0);
        layout.Controls.Add(unassignedAppsFlowPanel, 1, 0);
        unassignedAppsContainerPanel.Controls.Add(layout);

        channelsContainer.Controls.Add(unassignedAppsContainerPanel, 0, 1);
        channelsContainer.Controls.Add(channelsTable, 0, 2);
        channelsContainer.ResumeLayout();
    }

    private void BuildChannelPanels()
    {
        channelsTable.SuspendLayout();
        channelsTable.Controls.Clear();
        channelControlsByName.Clear();
        channelsTable.ColumnStyles.Clear();
        channelsTable.RowStyles.Clear();
        channelsTable.ColumnCount = channels.Count + 1;
        channelsTable.RowCount = 1;
        channelsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        for (var column = 0; column < channelsTable.ColumnCount; column++)
        {
            channelsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / channelsTable.ColumnCount));
        }

        masterStripControl = new DualMixChannelStripControl
        {
            ChannelName = "Master",
            IconText = "MASTER",
            AccentColor = Color.FromArgb(86, 169, 255),
            MonitorPercent = GainToPercent(settings.MonitorMix.MasterGain),
            StreamPercent = GainToPercent(settings.StreamMix.MasterGain),
            MonitorMuted = settings.MonitorMix.MasterMuted,
            StreamMuted = settings.StreamMix.MasterMuted,
            EndpointComboBoxVisible = false
        };
        masterStripControl.SetMixControlsEnabled(enabled: true);
        channelsTable.Controls.Add(masterStripControl, 0, 0);

        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var controls = CreateChannelControls(channel);
            channelControlsByName[channel.Name] = controls;

            channelsTable.Controls.Add(controls.StripControl, index + 1, 0);
        }

        UpdateMasterStripSummary();
        UpdateMixerAppSummaries();
        channelsTable.ResumeLayout();
    }

    private ChannelControls CreateChannelControls(MixerChannel channel)
    {
        var stripControl = new DualMixChannelStripControl
        {
            ChannelName = channel.Name,
            IconText = GetChannelIconText(channel.Name),
            AccentColor = GetChannelAccentColor(channel.Name),
            MonitorPercent = GainToPercent(settings.MonitorMix.ChannelGains.GetValueOrDefault(channel.Name, 0.5f)),
            StreamPercent = GainToPercent(settings.StreamMix.ChannelGains.GetValueOrDefault(channel.Name, 1.0f)),
            MonitorMuted = settings.MonitorMix.ChannelMutes.GetValueOrDefault(channel.Name),
            StreamMuted = settings.StreamMix.ChannelMutes.GetValueOrDefault(channel.Name),
            EndpointComboBoxVisible = true
        };
        stripControl.SetMixControlsEnabled(enabled: true);

        var controls = new ChannelControls(channel, stripControl);

        stripControl.EndpointComboBox.SelectedIndexChanged += (_, _) => HandleEndpointSelectionChanged(controls);
        stripControl.MonitorTrackBar.ValueChanged += (_, _) => HandleVolumeChanged(controls);
        stripControl.MonitorMuteButton.Click += (_, _) => ToggleChannelMute(controls);
        stripControl.StreamTrackBar.ValueChanged += (_, _) => HandleMixerStreamVolumeChanged(controls);
        stripControl.StreamMuteButton.Click += (_, _) => ToggleMixerStreamMute(controls);
        return controls;
    }

    private void RegisterMixerDropTargets()
    {
        if (mixerDropTargetsRegistered)
        {
            return;
        }

        RegisterUnassignedDropTarget(unassignedAppsContainerPanel);
        RegisterUnassignedDropTarget(unassignedAppsFlowPanel);
        RegisterUnassignedDropTarget(unassignedAppsTitleLabel);
        RegisterUnassignedDropTargetForChildren(unassignedAppsContainerPanel);

        foreach (var controls in channelControlsByName.Values)
        {
            RegisterChannelDropTarget(controls.StripControl.AppDropArea, controls.Channel.Name);
            RegisterChannelDropTarget(controls.StripControl.AppChipsPanel, controls.Channel.Name);
            RegisterChannelDropTargetForChildren(controls.StripControl.AppChipsPanel, controls.Channel.Name);
        }

        mixerDropTargetsRegistered = true;
    }

    private void RegisterChannelDropTarget(Control target, string channelName)
    {
        target.AllowDrop = true;
        target.DragEnter += (_, args) => HandleChannelDragOver(args, channelName);
        target.DragOver += (_, args) => HandleChannelDragOver(args, channelName);
        target.DragLeave += (_, _) => SetChannelDropHighlight(channelName, highlighted: false);
        target.DragDrop += (_, args) =>
        {
            SetChannelDropHighlight(channelName, highlighted: false);
            if (TryGetDraggedApp(args, out var draggedApp))
            {
                AssignAppFromMixer(draggedApp.ProcessName, channelName);
            }
        };

    }

    private void RegisterChannelDropTargetForChildren(Control control, string channelName)
    {
        foreach (Control child in control.Controls)
        {
            RegisterChannelDropTarget(child, channelName);
            RegisterChannelDropTargetForChildren(child, channelName);
        }
    }

    private void RegisterUnassignedDropTarget(Control target)
    {
        target.AllowDrop = true;
        target.DragEnter += (_, args) => HandleUnassignedDragOver(args);
        target.DragOver += (_, args) => HandleUnassignedDragOver(args);
        target.DragLeave += (_, _) => SetUnassignedDropHighlight(highlighted: false);
        target.DragDrop += (_, args) =>
        {
            SetUnassignedDropHighlight(highlighted: false);
            if (TryGetDraggedApp(args, out var draggedApp))
            {
                AssignAppFromMixer(draggedApp.ProcessName, channelName: null);
            }
        };
    }

    private void HandleChannelDragOver(DragEventArgs args, string channelName)
    {
        if (!TryGetDraggedApp(args, out _))
        {
            args.Effect = DragDropEffects.None;
            SetChannelDropHighlight(channelName, highlighted: false);
            return;
        }

        args.Effect = DragDropEffects.Move;
        SetChannelDropHighlight(channelName, highlighted: true);
    }

    private void HandleUnassignedDragOver(DragEventArgs args)
    {
        if (!TryGetDraggedApp(args, out _))
        {
            args.Effect = DragDropEffects.None;
            SetUnassignedDropHighlight(highlighted: false);
            return;
        }

        args.Effect = DragDropEffects.Move;
        SetUnassignedDropHighlight(highlighted: true);
    }

    private static bool TryGetDraggedApp(DragEventArgs args, out AppChipDragData draggedApp)
    {
        draggedApp = null!;
        if (args.Data is null || !args.Data.GetDataPresent(typeof(AppChipDragData)))
        {
            return false;
        }

        draggedApp = args.Data.GetData(typeof(AppChipDragData)) as AppChipDragData ?? null!;
        return draggedApp is not null && !string.IsNullOrWhiteSpace(draggedApp.ProcessName);
    }

    private void SetChannelDropHighlight(string channelName, bool highlighted)
    {
        if (channelControlsByName.TryGetValue(channelName, out var controls))
        {
            controls.StripControl.SetDropHighlight(highlighted);
        }
    }

    private void SetUnassignedDropHighlight(bool highlighted)
    {
        if (unassignedAppsContainerPanel is null || unassignedAppsFlowPanel is null)
        {
            return;
        }

        var color = highlighted
            ? Color.FromArgb(37, 42, 51)
            : Color.FromArgb(32, 36, 43);
        unassignedAppsContainerPanel.BackColor = color;
        unassignedAppsFlowPanel.BackColor = highlighted
            ? Color.FromArgb(37, 42, 51)
            : Color.FromArgb(24, 27, 33);
    }

    private static string GetChannelIconText(string channelName)
    {
        return channelName.ToUpperInvariant() switch
        {
            "GAME" => "GAME",
            "CHAT" => "CHAT",
            "MEDIA" => "MEDIA",
            "MUSIC" => "MUSIC",
            _ => "CH"
        };
    }

    private static Color GetChannelAccentColor(string channelName)
    {
        return channelName.ToUpperInvariant() switch
        {
            "GAME" => Color.FromArgb(84, 220, 160),
            "CHAT" => Color.FromArgb(80, 145, 255),
            "MEDIA" => Color.FromArgb(232, 88, 180),
            "MUSIC" => Color.FromArgb(178, 118, 255),
            _ => Color.DeepSkyBlue
        };
    }

    private void UpdateMasterStripSummary()
    {
        if (masterStripControl is null)
        {
            return;
        }

        var output = FindMonitorOutputEndpoint();
        var streamOutput = FindStreamOutputEndpoint();
        masterStripControl.AppSummary =
            $"Monitor: {output?.FriendlyName ?? "No output"}{Environment.NewLine}Stream: {streamOutput?.FriendlyName ?? "No output"}";
    }

    private void UpdateMeters()
    {
        if (masterStripControl is not null)
        {
            masterStripControl.MonitorVuMeter.Value = PeakToPercent(monitorMixEngine.GetMasterPeak());
            masterStripControl.StreamVuMeter.Value = PeakToPercent(streamMixEngine.GetMasterPeak());
        }

        var peaks = monitorMixEngine.GetChannelPeaks();
        foreach (var controls in channelControlsByName.Values)
        {
            controls.StripControl.MonitorVuMeter.Value = peaks.TryGetValue(controls.Channel.Name, out var peak)
                ? PeakToPercent(peak)
                : 0;
            controls.StripControl.StreamVuMeter.Value = PeakToPercent(streamMixEngine.GetChannelPeak(controls.Channel.Name));
        }
    }

    private static int PeakToPercent(float peak)
        => Math.Clamp((int)Math.Round(Math.Clamp(peak, 0f, 1f) * 100f, MidpointRounding.AwayFromZero), 0, 100);

    private static int GainToPercent(float gain)
        => Math.Clamp((int)Math.Round(Math.Clamp(gain, 0f, 1f) * 100f, MidpointRounding.AwayFromZero), 0, 100);

    private void RefreshEndpoints(bool applyFirstRunAutoMapping)
    {
        try
        {
            endpoints = audioEndpointController.GetRenderEndpoints();
            captureEndpoints = audioEndpointController.GetCaptureEndpoints();
            AppendLog($"Found {endpoints.Count} active render endpoint(s).");

            foreach (var endpoint in endpoints)
            {
                AppendLog($"Endpoint: {endpoint.FriendlyName} [{endpoint.Id}]");
            }

            AppendLog($"Found {captureEndpoints.Count} active capture endpoint(s).");
            foreach (var endpoint in captureEndpoints)
            {
                AppendLog($"Found capture endpoint: {endpoint.FriendlyName} [{endpoint.Id}]");
            }

            if (applyFirstRunAutoMapping)
            {
                ApplyFirstRunAutoMapping();
                ApplyFirstRunMonitorMapping();
            }

            ApplyMissingStreamDefaults();

            RefreshEndpointComboBoxes();
            RefreshMonitorDeviceComboBoxes();
            RefreshStreamDeviceComboBoxes();
            UpdateChannelControlAvailability();
            SaveSettings();
        }
        catch (Exception ex)
        {
            endpoints = [];
            captureEndpoints = [];
            AppendLog($"Endpoint refresh error: {ex.Message}");

            foreach (var controls in channelControlsByName.Values)
            {
                MarkEndpointUnavailable(controls, "Error");
            }
        }
    }

    private void ApplyFirstRunMonitorMapping()
    {
        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            if (!string.IsNullOrWhiteSpace(settings.MonitorMix.GetCaptureEndpointId(channelName)))
            {
                continue;
            }

            var match = FindDefaultCaptureEndpointForChannel(channelName);
            if (match is null)
            {
                continue;
            }

            settings.MonitorMix.SetCaptureEndpoint(channelName, match);
            AppendLog($"Auto-mapped monitor {channelName} input to {match.FriendlyName}.");
        }
    }

    private void ApplyMissingStreamDefaults()
    {
        if (!string.IsNullOrWhiteSpace(settings.StreamMix.OutputEndpointId) ||
            !string.IsNullOrWhiteSpace(settings.StreamMix.OutputEndpointFriendlyName))
        {
            return;
        }

        var recommendedOutput = FindRecommendedStreamOutputEndpoint();
        if (recommendedOutput is null)
        {
            return;
        }

        settings.StreamMix.OutputEndpointId = recommendedOutput.Id;
        settings.StreamMix.OutputEndpointFriendlyName = recommendedOutput.FriendlyName;
        AppendLog($"Auto-mapped stream output to {recommendedOutput.FriendlyName}.");
    }

    private AudioEndpoint? FindDefaultCaptureEndpointForChannel(string channelName)
    {
        var keywords = channelName.ToUpperInvariant() switch
        {
            "GAME" => new[] { "CABLE-A", "VB-Audio Cable A", "Cable A" },
            "CHAT" => new[] { "CABLE-B", "VB-Audio Cable B", "Cable B" },
            "MEDIA" => new[] { "CABLE-D", "VB-Audio Cable D", "Cable D" },
            "MUSIC" => new[] { "CABLE-C", "VB-Audio Cable C", "Cable C" },
            _ => []
        };

        return captureEndpoints.FirstOrDefault(endpoint =>
            keywords.Any(keyword => endpoint.FriendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }

    private void ApplyFirstRunAutoMapping()
    {
        var usedEndpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            var match = FindDefaultEndpointForChannel(channel.Name, usedEndpointIds);
            if (match is null)
            {
                continue;
            }

            channel.SelectedEndpointId = match.Id;
            channel.SelectedEndpointName = match.FriendlyName;
            usedEndpointIds.Add(match.Id);
            AppendLog($"Auto-mapped {channel.Name} to {match.FriendlyName}.");
        }
    }

    private AudioEndpoint? FindDefaultEndpointForChannel(string channelName, ISet<string> usedEndpointIds)
    {
        var keywords = channelName.ToUpperInvariant() switch
        {
            "GAME" => new[] { "CABLE-A", "VB-Audio Cable A", "Cable A" },
            "CHAT" => new[] { "CABLE-B", "VB-Audio Cable B", "Cable B" },
            "MEDIA" => new[] { "CABLE-D", "VB-Audio Cable D", "Cable D" },
            "MUSIC" => new[] { "CABLE-C", "VB-Audio Cable C", "Cable C" },
            _ => []
        };

        return endpoints.FirstOrDefault(endpoint =>
            !usedEndpointIds.Contains(endpoint.Id) &&
            keywords.Any(keyword => endpoint.FriendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }

    private void RefreshEndpointComboBoxes()
    {
        foreach (var controls in channelControlsByName.Values)
        {
            controls.IsUpdating = true;
            var comboBox = controls.StripControl.EndpointComboBox;
            comboBox.BeginUpdate();
            comboBox.Items.Clear();
            comboBox.Items.Add(EndpointChoice.Empty);

            foreach (var endpoint in endpoints)
            {
                comboBox.Items.Add(new EndpointChoice(endpoint));
            }

            var selectedEndpoint = FindSelectedEndpoint(controls.Channel);
            comboBox.SelectedItem = selectedEndpoint is null
                ? EndpointChoice.Empty
                : FindChoice(comboBox, selectedEndpoint.Id) ?? EndpointChoice.Empty;
            comboBox.DropDownWidth = Math.Max(comboBox.DropDownWidth, 360);
            comboBox.EndUpdate();
            controls.IsUpdating = false;

            if (selectedEndpoint is null)
            {
                MarkEndpointUnavailable(controls, "Not found");
                continue;
            }

            controls.Channel.SelectedEndpointId = selectedEndpoint.Id;
            controls.Channel.SelectedEndpointName = selectedEndpoint.FriendlyName;
            SyncChannelFromEndpoint(controls, logErrors: true);
        }
    }

    private void RefreshMonitorDeviceComboBoxes()
    {
        updatingMonitorUi = true;
        PopulateMonitorOutputComboBox();
        PopulateMonitorInputComboBox(monitorGameInputComboBox, "Game");
        PopulateMonitorInputComboBox(monitorChatInputComboBox, "Chat");
        PopulateMonitorInputComboBox(monitorMusicInputComboBox, "Music");
        PopulateMonitorInputComboBox(monitorMediaInputComboBox, "Media");

        channelSliderModeComboBox.SelectedItem = NormalizeSliderMode(settings.MonitorMix.ChannelSliderMode);
        monitorLatencyNumericUpDown.Value = Math.Clamp(settings.MonitorMix.LatencyMs, 10, 500);
        monitorExclusiveCheckBox.Checked = settings.MonitorMix.ExclusiveOutput;
        SyncMonitorMixControlsFromSettings();
        updatingMonitorUi = false;

        ApplyMonitorSettingsToEngine();
        UpdateMonitorWarning();
        UpdateMasterStripSummary();
    }

    private void SyncMonitorMixControlsFromSettings()
    {
        masterStripControl.MonitorPercent = GainToPercent(settings.MonitorMix.MasterGain);
        masterStripControl.MonitorMuted = settings.MonitorMix.MasterMuted;

        foreach (var controls in channelControlsByName.Values)
        {
            var volumePercent = GainToPercent(settings.MonitorMix.ChannelGains.GetValueOrDefault(controls.Channel.Name, 0.5f));
            var isMuted = settings.MonitorMix.ChannelMutes.GetValueOrDefault(controls.Channel.Name);
            controls.Channel.VolumePercent = volumePercent;
            controls.Channel.IsMuted = isMuted;
            SetVolumeUiValue(controls, volumePercent);
            SetMuteUiValue(controls, isMuted);
        }
    }

    private void PopulateMonitorOutputComboBox()
    {
        monitorOutputComboBox.BeginUpdate();
        monitorOutputComboBox.Items.Clear();
        monitorOutputComboBox.Items.Add(EndpointChoice.Empty);

        foreach (var endpoint in endpoints.Where(endpoint => !IsVbCableEndpoint(endpoint)))
        {
            monitorOutputComboBox.Items.Add(new EndpointChoice(endpoint));
        }

        var selectedEndpoint = FindMonitorOutputEndpoint();
        if (selectedEndpoint is not null && !monitorOutputComboBox.Items.OfType<EndpointChoice>().Any(choice =>
                choice.Endpoint?.Id.Equals(selectedEndpoint.Id, StringComparison.OrdinalIgnoreCase) == true))
        {
            monitorOutputComboBox.Items.Add(new EndpointChoice(selectedEndpoint));
        }

        monitorOutputComboBox.SelectedItem = selectedEndpoint is null
            ? EndpointChoice.Empty
            : FindChoice(monitorOutputComboBox, selectedEndpoint.Id);
        monitorOutputComboBox.EndUpdate();
    }

    private void PopulateMonitorInputComboBox(ComboBox comboBox, string channelName)
    {
        comboBox.BeginUpdate();
        comboBox.Items.Clear();
        comboBox.Items.Add(EndpointChoice.Empty);

        foreach (var endpoint in captureEndpoints)
        {
            comboBox.Items.Add(new EndpointChoice(endpoint));
        }

        var selectedEndpoint = FindMonitorCaptureEndpoint(channelName);
        comboBox.SelectedItem = selectedEndpoint is null
            ? EndpointChoice.Empty
            : FindChoice(comboBox, selectedEndpoint.Id);
        comboBox.EndUpdate();
    }

    private AudioEndpoint? FindMonitorOutputEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(settings.MonitorMix.OutputEndpointId))
        {
            var byId = endpoints.FirstOrDefault(endpoint =>
                endpoint.Id.Equals(settings.MonitorMix.OutputEndpointId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.MonitorMix.OutputEndpointFriendlyName))
        {
            return endpoints.FirstOrDefault(endpoint =>
                endpoint.FriendlyName.Equals(settings.MonitorMix.OutputEndpointFriendlyName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private AudioEndpoint? FindMonitorCaptureEndpoint(string channelName)
    {
        var endpointId = settings.MonitorMix.GetCaptureEndpointId(channelName);
        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            var byId = captureEndpoints.FirstOrDefault(endpoint =>
                endpoint.Id.Equals(endpointId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        var friendlyName = settings.MonitorMix.GetCaptureEndpointFriendlyName(channelName);
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            return captureEndpoints.FirstOrDefault(endpoint =>
                endpoint.FriendlyName.Equals(friendlyName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void RefreshStreamDeviceComboBoxes()
    {
        updatingStreamUi = true;
        PopulateStreamOutputComboBox();
        streamLatencyNumericUpDown.Value = Math.Clamp(settings.StreamMix.LatencyMs, 10, 500);
        streamLatencyValueLabel.Text = $"{settings.StreamMix.LatencyMs} ms";
        masterStripControl.StreamPercent = GainToPercent(settings.StreamMix.MasterGain);
        masterStripControl.StreamMuted = settings.StreamMix.MasterMuted;

        foreach (var controls in channelControlsByName.Values)
        {
            controls.IsUpdating = true;
            controls.StripControl.StreamPercent = GainToPercent(
                settings.StreamMix.ChannelGains.GetValueOrDefault(controls.Channel.Name, 1.0f));
            controls.StripControl.StreamMuted = settings.StreamMix.ChannelMutes.GetValueOrDefault(controls.Channel.Name);
            controls.IsUpdating = false;
        }

        updatingStreamUi = false;

        ApplyStreamSettingsToEngine();
        UpdateStreamWarning();
        UpdateStreamMasterStripSummary();
    }

    private void PopulateStreamOutputComboBox()
    {
        streamOutputComboBox.BeginUpdate();
        streamOutputComboBox.Items.Clear();
        streamOutputComboBox.Items.Add(EndpointChoice.Empty);

        foreach (var endpoint in endpoints)
        {
            streamOutputComboBox.Items.Add(new EndpointChoice(endpoint));
        }

        var selectedEndpoint = FindStreamOutputEndpoint();
        streamOutputComboBox.SelectedItem = selectedEndpoint is null
            ? EndpointChoice.Empty
            : FindChoice(streamOutputComboBox, selectedEndpoint.Id);
        streamOutputComboBox.EndUpdate();
    }

    private AudioEndpoint? FindStreamOutputEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(settings.StreamMix.OutputEndpointId))
        {
            var byId = endpoints.FirstOrDefault(endpoint =>
                endpoint.Id.Equals(settings.StreamMix.OutputEndpointId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.StreamMix.OutputEndpointFriendlyName))
        {
            return endpoints.FirstOrDefault(endpoint =>
                endpoint.FriendlyName.Equals(settings.StreamMix.OutputEndpointFriendlyName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private AudioEndpoint? FindStreamCaptureEndpoint(string channelName)
        => FindDefaultCaptureEndpointForChannel(channelName);

    private AudioEndpoint? FindRecommendedStreamOutputEndpoint()
    {
        return endpoints.FirstOrDefault(IsRecommendedStreamOutputEndpoint)
            ?? endpoints.FirstOrDefault(endpoint =>
                !IsChannelCableEndpoint(endpoint) &&
                endpoint.FriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase) &&
                endpoint.FriendlyName.Contains("Input", StringComparison.OrdinalIgnoreCase))
            ?? endpoints.FirstOrDefault(endpoint =>
                !IsChannelCableEndpoint(endpoint) &&
                endpoint.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
    }

    private void HandleMonitorOutputChanged()
    {
        if (updatingMonitorUi)
        {
            return;
        }

        var endpoint = (monitorOutputComboBox.SelectedItem as EndpointChoice)?.Endpoint;
        settings.MonitorMix.OutputEndpointId = endpoint?.Id;
        settings.MonitorMix.OutputEndpointFriendlyName = endpoint?.FriendlyName;
        ApplyMonitorSettingsToEngine();
        UpdateMonitorWarning();
        UpdateMasterStripSummary();
        SaveSettings();

        if (endpoint is not null)
        {
            AppendLog($"Monitor output = {endpoint.FriendlyName}");
        }
    }

    private void HandleMonitorInputChanged(string channelName, ComboBox comboBox)
    {
        if (updatingMonitorUi)
        {
            return;
        }

        var endpoint = (comboBox.SelectedItem as EndpointChoice)?.Endpoint;
        settings.MonitorMix.SetCaptureEndpoint(channelName, endpoint);
        ApplyMonitorSettingsToEngine();
        SaveSettings();

        if (endpoint is not null)
        {
            AppendLog($"Monitor {channelName} input = {endpoint.FriendlyName}");
        }
    }

    private void HandleMonitorMasterGainChanged()
    {
        if (updatingMonitorUi)
        {
            return;
        }

        var volumePercent = masterStripControl.MonitorTrackBar.Value;
        settings.MonitorMix.MasterGain = volumePercent / 100f;
        SetMonitorMasterVolumeUiValue(volumePercent);
        monitorMixEngine.SetMasterGain(settings.MonitorMix.MasterGain);
        SaveSettings();
        AppendLog($"Monitor master gain = {volumePercent}%");
    }

    private void ToggleMonitorMasterMute()
    {
        settings.MonitorMix.MasterMuted = !settings.MonitorMix.MasterMuted;
        monitorMixEngine.SetMasterMute(settings.MonitorMix.MasterMuted);
        SetMonitorMasterMuteUiValue(settings.MonitorMix.MasterMuted);
        SaveSettings();
        AppendLog($"Monitor master mute = {(settings.MonitorMix.MasterMuted ? "on" : "off")}");
    }

    private void HandleChannelSliderModeChanged()
    {
        if (updatingMonitorUi)
        {
            return;
        }

        settings.MonitorMix.ChannelSliderMode = NormalizeSliderMode(channelSliderModeComboBox.SelectedItem?.ToString());
        SaveSettings();
        AppendLog($"Channel slider mode = {settings.MonitorMix.ChannelSliderMode}");
    }

    private void SetMonitorMasterVolumeUiValue(int volumePercent)
    {
        updatingMonitorUi = true;
        masterStripControl.MonitorPercent = volumePercent;
        updatingMonitorUi = false;
    }

    private void SetMonitorMasterMuteUiValue(bool isMuted)
    {
        masterStripControl.MonitorMuted = isMuted;
    }

    private void ApplyMonitorSettingsToEngine()
    {
        monitorMixEngine.LatencyMs = settings.MonitorMix.LatencyMs;
        monitorMixEngine.UseExclusiveOutput = settings.MonitorMix.ExclusiveOutput;

        monitorMixEngine.SetOutputDevice(settings.MonitorMix.OutputEndpointId ?? string.Empty);

        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            var endpointId = settings.MonitorMix.GetCaptureEndpointId(channelName);
            monitorMixEngine.SetChannelInput(channelName, endpointId ?? string.Empty);

            monitorMixEngine.SetChannelGain(
                channelName,
                settings.MonitorMix.ChannelGains.GetValueOrDefault(channelName, 0.5f));
            monitorMixEngine.SetChannelMute(
                channelName,
                settings.MonitorMix.ChannelMutes.GetValueOrDefault(channelName));
        }

        monitorMixEngine.SetMasterGain(settings.MonitorMix.MasterGain);
        monitorMixEngine.SetMasterMute(settings.MonitorMix.MasterMuted);
    }

    private void StartMonitor()
    {
        if (monitorOperationInProgress)
        {
            return;
        }

        try
        {
            ValidateMonitorSelection();
            ApplyMonitorSettingsToEngine();
            monitorMixEngine.Start();
            monitorStatusValueLabel.Text = "Running";
            monitorStatusValueLabel.ForeColor = Color.ForestGreen;
            SetMonitorButtons(isRunning: true, operationInProgress: false);
            SaveSettings();
        }
        catch (Exception ex)
        {
            monitorStatusValueLabel.Text = "Error";
            monitorStatusValueLabel.ForeColor = Color.Firebrick;
            SetMonitorButtons(isRunning: monitorMixEngine.IsRunning, operationInProgress: false);
            AppendLog($"Monitor start error: {ex.Message}");
        }
    }

    private async Task StopMonitorAsync()
    {
        if (monitorOperationInProgress)
        {
            return;
        }

        monitorOperationInProgress = true;
        monitorStatusValueLabel.Text = "Stopping...";
        monitorStatusValueLabel.ForeColor = Color.DarkOrange;
        SetMonitorButtons(isRunning: true, operationInProgress: true);

        try
        {
            await monitorMixEngine.StopAsync();
            monitorStatusValueLabel.Text = "Stopped";
            monitorStatusValueLabel.ForeColor = Color.DimGray;
        }
        catch (Exception ex)
        {
            monitorStatusValueLabel.Text = "Error";
            monitorStatusValueLabel.ForeColor = Color.Firebrick;
            AppendLog($"Monitor stop error: {ex.Message}");
        }
        finally
        {
            monitorOperationInProgress = false;
            SetMonitorButtons(isRunning: monitorMixEngine.IsRunning, operationInProgress: false);
        }
    }

    private async Task RestartMonitorAsync()
    {
        if (monitorOperationInProgress)
        {
            return;
        }

        monitorOperationInProgress = true;
        monitorStatusValueLabel.Text = "Restarting...";
        monitorStatusValueLabel.ForeColor = Color.DarkOrange;
        SetMonitorButtons(isRunning: true, operationInProgress: true);

        try
        {
            ValidateMonitorSelection();
            await monitorMixEngine.StopAsync();
            ApplyMonitorSettingsToEngine();
            monitorMixEngine.Start();
            monitorStatusValueLabel.Text = "Running";
            monitorStatusValueLabel.ForeColor = Color.ForestGreen;
        }
        catch (Exception ex)
        {
            monitorStatusValueLabel.Text = "Error";
            monitorStatusValueLabel.ForeColor = Color.Firebrick;
            AppendLog($"Monitor restart error: {ex.Message}");
        }
        finally
        {
            monitorOperationInProgress = false;
            SetMonitorButtons(isRunning: monitorMixEngine.IsRunning, operationInProgress: false);
        }
    }

    private void SetMonitorButtons(bool isRunning, bool operationInProgress)
    {
        startMonitorButton.Enabled = !operationInProgress && !isRunning;
        stopMonitorButton.Enabled = !operationInProgress && isRunning;
        restartMonitorButton.Enabled = !operationInProgress;
    }

    private void ValidateMonitorSelection()
    {
        var output = FindMonitorOutputEndpoint();
        if (output is null)
        {
            throw new InvalidOperationException("Select a physical monitor output device.");
        }

        if (IsVbCableEndpoint(output))
        {
            throw new InvalidOperationException("Do not monitor into a VB-CABLE endpoint. This may create feedback loop.");
        }

        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            if (FindMonitorCaptureEndpoint(channelName) is null)
            {
                throw new InvalidOperationException($"Select monitor input for {channelName}.");
            }
        }
    }

    private void UpdateMonitorWarning()
    {
        var output = FindMonitorOutputEndpoint();
        monitorWarningLabel.ForeColor = output is not null && IsVbCableEndpoint(output)
            ? Color.DarkOrange
            : Color.FromArgb(190, 196, 205);
        monitorWarningLabel.Text = output is not null && IsVbCableEndpoint(output)
            ? "Do not monitor into a VB-CABLE endpoint. This may create feedback loop."
            : "Mixer tab controls Monitor Mix only. App Session Volume changes Windows app volume before both Monitor and Stream mixes; use Monitor/Stream gains for independent mixes.";
    }

    private void HandleStreamOutputChanged()
    {
        if (updatingStreamUi)
        {
            return;
        }

        var endpoint = (streamOutputComboBox.SelectedItem as EndpointChoice)?.Endpoint;
        settings.StreamMix.OutputEndpointId = endpoint?.Id;
        settings.StreamMix.OutputEndpointFriendlyName = endpoint?.FriendlyName;
        ApplyStreamSettingsToEngine();
        UpdateStreamWarning();
        UpdateStreamMasterStripSummary();
        UpdateMasterStripSummary();
        SaveSettings();

        if (endpoint is not null)
        {
            AppendLog($"Stream output = {endpoint.FriendlyName}");
        }
    }

    private void HandleMonitorLatencyChanged()
    {
        if (updatingMonitorUi)
        {
            return;
        }

        settings.MonitorMix.LatencyMs = (int)monitorLatencyNumericUpDown.Value;
        monitorMixEngine.LatencyMs = settings.MonitorMix.LatencyMs;
        SaveSettings();

        if (monitorMixEngine.IsRunning)
        {
            AppendLog("Monitor latency updated. Restart Monitor to apply it to the active WASAPI output.");
        }
    }

    private void HandleMonitorExclusiveChanged()
    {
        if (updatingMonitorUi)
        {
            return;
        }

        settings.MonitorMix.ExclusiveOutput = monitorExclusiveCheckBox.Checked;
        monitorMixEngine.UseExclusiveOutput = settings.MonitorMix.ExclusiveOutput;
        SaveSettings();

        if (monitorMixEngine.IsRunning)
        {
            AppendLog("Monitor output mode updated. Restart Monitor to apply it.");
        }
    }

    private void HandleStreamLatencyChanged()
    {
        if (updatingStreamUi)
        {
            return;
        }

        settings.StreamMix.LatencyMs = (int)streamLatencyNumericUpDown.Value;
        streamLatencyValueLabel.Text = $"{settings.StreamMix.LatencyMs} ms";
        streamMixEngine.LatencyMs = settings.StreamMix.LatencyMs;
        SaveSettings();

        if (streamMixEngine.IsRunning)
        {
            AppendLog("Stream latency updated. Restart Stream Mix to apply it to the active WASAPI output.");
        }
    }

    private void ApplyStreamMasterGain(int volumePercent)
    {
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        settings.StreamMix.MasterGain = volumePercent / 100f;
        SetMixerStreamMasterVolumeUiValue(volumePercent);
        streamMixEngine.SetMasterGain(settings.StreamMix.MasterGain);
        SaveSettings();
        AppendLog($"Stream master gain = {volumePercent}%");
    }

    private void HandleMixerStreamMasterGainChanged()
    {
        if (updatingStreamUi)
        {
            return;
        }

        var volumePercent = masterStripControl.StreamTrackBar.Value;
        ApplyStreamMasterGain(volumePercent);
    }

    private void ToggleMixerStreamMasterMute()
    {
        settings.StreamMix.MasterMuted = !settings.StreamMix.MasterMuted;
        streamMixEngine.SetMasterMute(settings.StreamMix.MasterMuted);
        SetMixerStreamMasterMuteUiValue(settings.StreamMix.MasterMuted);
        SaveSettings();
        AppendLog($"Stream master mute = {(settings.StreamMix.MasterMuted ? "on" : "off")}");
    }

    private void HandleMixerStreamVolumeChanged(ChannelControls controls)
    {
        if (controls.IsUpdating)
        {
            return;
        }

        ApplyStreamChannelGain(controls.Channel.Name, controls.StripControl.StreamTrackBar.Value);
    }

    private void ApplyStreamChannelGain(string channelName, int volumePercent)
    {
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        var gain = volumePercent / 100f;
        settings.StreamMix.ChannelGains[channelName] = gain;
        SetMixerStreamChannelVolumeUiValue(channelName, volumePercent);
        streamMixEngine.SetChannelGain(channelName, gain);
        SaveSettings();
        AppendLog($"{channelName} stream gain = {volumePercent}%");
    }

    private void ToggleMixerStreamMute(ChannelControls controls)
    {
        var isMuted = !settings.StreamMix.ChannelMutes.GetValueOrDefault(controls.Channel.Name);
        settings.StreamMix.ChannelMutes[controls.Channel.Name] = isMuted;
        streamMixEngine.SetChannelMute(controls.Channel.Name, isMuted);
        SetMixerStreamChannelMuteUiValue(controls.Channel.Name, isMuted);
        SaveSettings();
        AppendLog($"{controls.Channel.Name} stream mute = {(isMuted ? "on" : "off")}");
    }

    private void ApplyStreamSettingsToEngine()
    {
        streamMixEngine.LatencyMs = settings.StreamMix.LatencyMs;

        var outputEndpoint = FindStreamOutputEndpoint();
        streamMixEngine.SetOutputDevice(outputEndpoint?.Id ?? string.Empty);

        foreach (var channelName in StreamChannelNames)
        {
            var endpointId = FindStreamCaptureEndpoint(channelName)?.Id;
            streamMixEngine.SetChannelInput(channelName, endpointId ?? string.Empty);
            streamMixEngine.SetChannelGain(
                channelName,
                settings.StreamMix.ChannelGains.GetValueOrDefault(channelName, 1.0f));
            streamMixEngine.SetChannelMute(
                channelName,
                settings.StreamMix.ChannelMutes.GetValueOrDefault(channelName));
        }

        streamMixEngine.SetMasterGain(settings.StreamMix.MasterGain);
        streamMixEngine.SetMasterMute(settings.StreamMix.MasterMuted);
    }

    private void StartStreamMix()
    {
        if (streamOperationInProgress)
        {
            return;
        }

        try
        {
            ValidateStreamSelection();
            ApplyStreamSettingsToEngine();
            streamMixEngine.Start();
            streamStatusValueLabel.Text = "Running";
            streamStatusValueLabel.ForeColor = Color.ForestGreen;
            SetStreamButtons(isRunning: true, operationInProgress: false);
            SaveSettings();
        }
        catch (Exception ex)
        {
            streamStatusValueLabel.Text = "Error";
            streamStatusValueLabel.ForeColor = Color.Firebrick;
            SetStreamButtons(isRunning: streamMixEngine.IsRunning, operationInProgress: false);
            AppendLog($"Stream start error: {ex.Message}");
        }
    }

    private async Task StopStreamMixAsync()
    {
        if (streamOperationInProgress)
        {
            return;
        }

        streamOperationInProgress = true;
        streamStatusValueLabel.Text = "Stopping...";
        streamStatusValueLabel.ForeColor = Color.DarkOrange;
        SetStreamButtons(isRunning: true, operationInProgress: true);

        try
        {
            await streamMixEngine.StopAsync();
            streamStatusValueLabel.Text = "Stopped";
            streamStatusValueLabel.ForeColor = Color.DimGray;
        }
        catch (Exception ex)
        {
            streamStatusValueLabel.Text = "Error";
            streamStatusValueLabel.ForeColor = Color.Firebrick;
            AppendLog($"Stream stop error: {ex.Message}");
        }
        finally
        {
            streamOperationInProgress = false;
            SetStreamButtons(isRunning: streamMixEngine.IsRunning, operationInProgress: false);
        }
    }

    private async Task RestartStreamMixAsync()
    {
        if (streamOperationInProgress)
        {
            return;
        }

        streamOperationInProgress = true;
        streamStatusValueLabel.Text = "Restarting...";
        streamStatusValueLabel.ForeColor = Color.DarkOrange;
        SetStreamButtons(isRunning: true, operationInProgress: true);

        try
        {
            ValidateStreamSelection();
            await streamMixEngine.StopAsync();
            ApplyStreamSettingsToEngine();
            streamMixEngine.Start();
            streamStatusValueLabel.Text = "Running";
            streamStatusValueLabel.ForeColor = Color.ForestGreen;
        }
        catch (Exception ex)
        {
            streamStatusValueLabel.Text = "Error";
            streamStatusValueLabel.ForeColor = Color.Firebrick;
            AppendLog($"Stream restart error: {ex.Message}");
        }
        finally
        {
            streamOperationInProgress = false;
            SetStreamButtons(isRunning: streamMixEngine.IsRunning, operationInProgress: false);
        }
    }

    private void SetStreamButtons(bool isRunning, bool operationInProgress)
    {
        startStreamButton.Enabled = !operationInProgress && !isRunning;
        stopStreamButton.Enabled = !operationInProgress && isRunning;
        restartStreamButton.Enabled = !operationInProgress;
    }

    private void ValidateStreamSelection()
    {
        var output = FindStreamOutputEndpoint();
        if (output is null)
        {
            throw new InvalidOperationException("Select an unused virtual cable output for OBS, for example CABLE Input.");
        }

        if (IsChannelCableEndpoint(output))
        {
            throw new InvalidOperationException("Do not output Stream Mix into a channel cable. This may create feedback or routing conflicts.");
        }

        foreach (var channelName in StreamChannelNames)
        {
            if (FindStreamCaptureEndpoint(channelName) is null)
            {
                throw new InvalidOperationException($"Stream input for {channelName} was not found.");
            }
        }
    }

    private void UpdateStreamWarning()
    {
        var output = FindStreamOutputEndpoint();
        if (output is null)
        {
            streamWarningLabel.ForeColor = Color.DarkOrange;
            streamWarningLabel.Text = "Select an unused virtual cable output for OBS, for example CABLE Input.";
            SetStreamIdleStatus("Stopped", Color.DimGray);
            return;
        }

        if (IsChannelCableEndpoint(output))
        {
            streamWarningLabel.ForeColor = Color.Firebrick;
            streamWarningLabel.Text = "Do not output Stream Mix into a channel cable. This may create feedback or routing conflicts.";
            SetStreamIdleStatus("Unsafe output selected", Color.Firebrick);
            return;
        }

        if (IsRecommendedStreamOutputEndpoint(output))
        {
            streamWarningLabel.ForeColor = Color.ForestGreen;
            streamWarningLabel.Text = "Recommended OBS stream output selected. In OBS, capture CABLE Output (VB-Audio Virtual Cable) and disable Desktop Audio.";
            SetStreamIdleStatus("Recommended OBS stream output selected.", Color.ForestGreen);
            return;
        }

        streamWarningLabel.ForeColor = Color.DarkOrange;
        streamWarningLabel.Text = "OBS should capture CABLE Output (VB-Audio Virtual Cable). Do not use CABLE-A/B/C/D Input for Stream Mix, and disable Desktop Audio in OBS.";
        SetStreamIdleStatus("Custom output selected", Color.DarkOrange);
    }

    private void SetStreamIdleStatus(string text, Color color)
    {
        if (streamMixEngine.IsRunning)
        {
            return;
        }

        streamStatusValueLabel.Text = text;
        streamStatusValueLabel.ForeColor = color;
    }

    private void UpdateStreamMasterStripSummary()
    {
        UpdateMasterStripSummary();
    }

    private void SetMixerStreamMasterVolumeUiValue(int volumePercent)
    {
        updatingStreamUi = true;
        masterStripControl.StreamPercent = volumePercent;
        updatingStreamUi = false;
    }

    private void SetMixerStreamMasterMuteUiValue(bool isMuted)
    {
        masterStripControl.StreamMuted = isMuted;
    }

    private void SetMixerStreamChannelVolumeUiValue(string channelName, int volumePercent)
    {
        if (!channelControlsByName.TryGetValue(channelName, out var controls))
        {
            return;
        }

        controls.IsUpdating = true;
        controls.StripControl.StreamPercent = volumePercent;
        controls.IsUpdating = false;
    }

    private void SetMixerStreamChannelMuteUiValue(string channelName, bool isMuted)
    {
        if (!channelControlsByName.TryGetValue(channelName, out var controls))
        {
            return;
        }

        controls.StripControl.StreamMuted = isMuted;
    }

    private static bool IsVbCableEndpoint(AudioEndpoint endpoint)
        => endpoint.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
           endpoint.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsRecommendedStreamOutputEndpoint(AudioEndpoint endpoint)
        => !IsChannelCableEndpoint(endpoint) &&
           endpoint.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) &&
           endpoint.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsChannelCableEndpoint(AudioEndpoint endpoint)
    {
        var name = endpoint.FriendlyName;
        return name.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CABLE-C", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CABLE-D", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Cable A", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Cable B", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Cable C", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Cable D", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSliderMode(string? mode)
    {
        return "Monitor Mix Gain";
    }

    private AudioEndpoint? FindSelectedEndpoint(MixerChannel channel)
    {
        if (!string.IsNullOrWhiteSpace(channel.SelectedEndpointId))
        {
            var byId = endpoints.FirstOrDefault(endpoint =>
                endpoint.Id.Equals(channel.SelectedEndpointId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(channel.SelectedEndpointName))
        {
            return endpoints.FirstOrDefault(endpoint =>
                endpoint.FriendlyName.Equals(channel.SelectedEndpointName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static EndpointChoice? FindChoice(ComboBox comboBox, string endpointId)
    {
        return comboBox.Items
            .OfType<EndpointChoice>()
            .FirstOrDefault(choice => choice.Endpoint?.Id.Equals(endpointId, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void HandleEndpointSelectionChanged(ChannelControls controls)
    {
        if (controls.IsUpdating)
        {
            return;
        }

        var endpoint = (controls.StripControl.EndpointComboBox.SelectedItem as EndpointChoice)?.Endpoint;
        controls.Channel.SelectedEndpointId = endpoint?.Id;
        controls.Channel.SelectedEndpointName = endpoint?.FriendlyName;

        if (endpoint is null)
        {
            MarkEndpointUnavailable(controls, "Not found");
            AppendLog($"{controls.Channel.Name} routing endpoint cleared.");
        }
        else
        {
            SyncChannelFromEndpoint(controls, logErrors: false);
            AppendLog($"{controls.Channel.Name} routing endpoint = {endpoint.FriendlyName}");
        }

        SaveRoutingRulesFromGrid(persist: false);
        BindAppSessionsGrid();
        UpdateChannelControlAvailability();
        SaveSettings();

        if (settings.AutoApplyRoutingRules)
        {
            _ = AutoApplyPendingRoutesAsync();
        }
    }

    private void SyncChannelFromEndpoint(ChannelControls controls, bool logErrors)
    {
        var endpointId = controls.Channel.SelectedEndpointId;
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            MarkEndpointUnavailable(controls, "Not found");
            return;
        }

        try
        {
            if (!endpoints.Any(endpoint => endpoint.Id.Equals(endpointId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Selected endpoint is not active.");
            }

            var volumePercent = GainToPercent(settings.MonitorMix.ChannelGains.GetValueOrDefault(controls.Channel.Name, 0.5f));
            var isMuted = settings.MonitorMix.ChannelMutes.GetValueOrDefault(controls.Channel.Name);
            controls.Channel.VolumePercent = volumePercent;
            controls.Channel.IsMuted = isMuted;
            SetVolumeUiValue(controls, volumePercent);
            SetMuteUiValue(controls, isMuted);
            SetEndpointControlsEnabled(controls, enabled: true);
            SetStatus(controls, isMuted ? "Muted" : "Found");
        }
        catch (Exception ex)
        {
            SetEndpointControlsEnabled(controls, enabled: true);
            SetStatus(controls, "Error");

            if (logErrors)
            {
                AppendLog($"{controls.Channel.Name} endpoint error: {ex.Message}");
            }
        }
    }

    private void HandleVolumeChanged(ChannelControls controls)
    {
        if (controls.IsUpdating || !controls.StripControl.MonitorTrackBar.Enabled)
        {
            return;
        }

        ApplyChannelVolume(controls, controls.StripControl.MonitorTrackBar.Value);
    }

    private void ApplyChannelVolume(ChannelControls controls, int volumePercent)
    {
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        controls.Channel.VolumePercent = volumePercent;
        SetVolumeUiValue(controls, volumePercent);

        var gain = volumePercent / 100f;
        settings.MonitorMix.ChannelGains[controls.Channel.Name] = gain;
        monitorMixEngine.SetChannelGain(controls.Channel.Name, gain);
        AppendLog($"{controls.Channel.Name} monitor gain = {volumePercent}%");
        SaveSettings();
    }

    private void ApplyEndpointVolumeFallback(ChannelControls controls, int volumePercent)
    {
        var endpointId = controls.Channel.SelectedEndpointId;
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            AppendLog($"{controls.Channel.Name} volume ignored: no active routed app session and no endpoint selected.");
            SaveSettings();
            return;
        }

        try
        {
            audioEndpointController.SetVolumePercent(endpointId, volumePercent);

            SetEndpointControlsEnabled(controls, enabled: true);
            SetStatus(controls, controls.Channel.IsMuted ? "Muted" : "Found");
            AppendLog($"{controls.Channel.Name} endpoint fallback volume = {volumePercent}%");
            SaveSettings();
        }
        catch (Exception ex)
        {
            SetStatus(controls, "Error");
            AppendLog($"{controls.Channel.Name} volume error: {ex.Message}");
        }
    }

    private void ToggleChannelMute(ChannelControls controls)
    {
        var isMuted = !settings.MonitorMix.ChannelMutes.GetValueOrDefault(controls.Channel.Name);
        ApplyChannelMute(controls, isMuted);
    }

    private void ApplyChannelMute(ChannelControls controls, bool isMuted)
    {
        settings.MonitorMix.ChannelMutes[controls.Channel.Name] = isMuted;
        monitorMixEngine.SetChannelMute(controls.Channel.Name, isMuted);
        controls.Channel.IsMuted = isMuted;
        SetMuteUiValue(controls, isMuted);
        SetStatus(controls, isMuted ? "Muted" : "Found");
        AppendLog($"{controls.Channel.Name} monitor mute = {(isMuted ? "on" : "off")}");
        SaveSettings();
    }

    private static void SetEndpointControlsEnabled(ChannelControls controls, bool enabled)
    {
        controls.StripControl.SetMixControlsEnabled(enabled);
        controls.StripControl.SetEndpointSelectorEnabled(enabled);
    }

    private void MarkEndpointUnavailable(ChannelControls controls, string status)
    {
        SetEndpointControlsEnabled(controls, enabled: true);
        SetStatus(controls, status);
    }

    private static void SetVolumeUiValue(ChannelControls controls, int volumePercent)
    {
        controls.IsUpdating = true;
        controls.StripControl.MonitorPercent = volumePercent;
        controls.IsUpdating = false;
    }

    private static void SetMuteUiValue(ChannelControls controls, bool isMuted)
    {
        controls.StripControl.MonitorMuted = isMuted;
    }

    private static void SetStatus(ChannelControls controls, string status)
    {
        controls.StripControl.EndpointComboBox.BackColor = status is "Error" or "Not found"
            ? Color.FromArgb(64, 31, 36)
            : Color.FromArgb(24, 27, 33);
    }

    private void LoadRoutingOptions()
    {
        enableExperimentalRoutingCheckBox.Checked = settings.EnableExperimentalAutomaticRouting;
        autoApplyRoutingRulesCheckBox.Checked = settings.AutoApplyRoutingRules;
        autoApplyRoutingTimer.Interval = (int)autoRefreshInterval.TotalMilliseconds;
        autoApplyRoutingTimer.Enabled = settings.AutoApplyRoutingRules;
    }

    private void SetupAppSessionsGrid()
    {
        appSessionsGrid.AutoGenerateColumns = false;
        appSessionsGrid.Columns.Clear();

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ProcessName",
            HeaderText = "Process",
            ReadOnly = true,
            FillWeight = 110
        });

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ProcessIds",
            HeaderText = "PIDs",
            ReadOnly = true,
            FillWeight = 90
        });

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SessionCount",
            HeaderText = "Sessions",
            ReadOnly = true,
            FillWeight = 55
        });

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CurrentEndpoints",
            HeaderText = "Current Endpoints",
            ReadOnly = true,
            FillWeight = 170
        });

        var assignedChannelColumn = new DataGridViewComboBoxColumn
        {
            Name = "AssignedChannel",
            HeaderText = "Channel",
            FlatStyle = FlatStyle.Flat,
            FillWeight = 85
        };
        assignedChannelColumn.Items.Add("None");
        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            assignedChannelColumn.Items.Add(channelName);
        }
        appSessionsGrid.Columns.Add(assignedChannelColumn);

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "TargetEndpoint",
            HeaderText = "Target Endpoint",
            ReadOnly = true,
            FillWeight = 150
        });

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Volume",
            HeaderText = "Volume",
            ReadOnly = true,
            FillWeight = 70
        });

        appSessionsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "Status",
            ReadOnly = true,
            FillWeight = 80
        });
    }

    private void RefreshAppSessions(bool logDetails = true)
    {
        if (isRefreshingApps)
        {
            AppendLog("Refresh apps skipped: already running.");
            return;
        }

        isRefreshingApps = true;
        try
        {
            if (logDetails)
            {
                AppendLog("Refresh apps started.");
            }

            var refreshedSessions = audioSessionController.GetAudioSessions(endpoints);
            ApplyRefreshedAppSessions(refreshedSessions, logDetails, bindGrid: true);
        }
        catch (Exception ex)
        {
            HandleAppRefreshError(ex, bindGrid: true);
        }
        finally
        {
            isRefreshingApps = false;
        }
    }

    private async Task<bool> RefreshAppSessionsAsync(
        bool logDetails,
        bool autoTriggered = false,
        bool force = false,
        bool bindGrid = false)
    {
        if (isRefreshingApps)
        {
            if (autoTriggered)
            {
                LogRoutingMessage("refresh-apps-skipped-running", "Refresh apps skipped: already running.", autoTriggered: true);
            }
            else
            {
                AppendLog("Refresh apps skipped: already running.");
            }

            return false;
        }

        var now = DateTime.UtcNow;
        if (autoTriggered && !force && now - lastRefreshAppsUtc < autoRefreshInterval)
        {
            return false;
        }

        isRefreshingApps = true;
        try
        {
            if (logDetails)
            {
                AppendLog("Refresh apps started.");
            }

            var endpointSnapshot = endpoints.ToList();
            var refreshedSessions = await Task.Run(() => audioSessionController.GetAudioSessions(endpointSnapshot));
            if (IsDisposed || Disposing)
            {
                return false;
            }

            ApplyRefreshedAppSessions(
                refreshedSessions,
                logDetails,
                bindGrid || mainTabControl.SelectedTab == routingTabPage);
            return true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                HandleAppRefreshError(ex, bindGrid || mainTabControl.SelectedTab == routingTabPage);
            }

            return false;
        }
        finally
        {
            isRefreshingApps = false;
        }
    }

    private void ApplyRefreshedAppSessions(
        IReadOnlyList<AudioAppSession> refreshedSessions,
        bool logDetails,
        bool bindGrid)
    {
        var oldGroups = appGroups;
        appSessions = refreshedSessions;
        AssignChannelsToSessions();
        var refreshedGroups = BuildAppGroups();
        var changeCount = CountAppGroupChanges(oldGroups, refreshedGroups);
        appGroups = refreshedGroups;

        if (bindGrid)
        {
            BindAppSessionsGrid();
        }

        UpdateChannelControlAvailability();
        lastRefreshAppsUtc = DateTime.UtcNow;

        if (logDetails || changeCount > 0)
        {
            AppendLog($"Refresh apps completed: {appGroups.Count} groups, {changeCount} changes.");
        }

        if (logDetails)
        {
            AppendLog($"Found {appSessions.Count} active audio app session(s) across {appGroups.Count} app group(s).");
            foreach (var group in appGroups)
            {
                AppendLog(
                    $"App group: {group.ProcessName}, PIDs = {FormatProcessIds(group.ProcessIds)}, sessions = {group.SessionCount}, endpoints = {group.CurrentEndpointsSummary}, target = {group.TargetEndpointFriendlyName}, volume = {group.VolumePercent}%, channel = {group.AssignedChannel}, status = {group.Status}");
            }
        }
    }

    private void HandleAppRefreshError(Exception ex, bool bindGrid)
    {
        appSessions = [];
        appGroups = [];
        if (bindGrid)
        {
            BindAppSessionsGrid();
        }

        UpdateChannelControlAvailability();
        AppendLog($"Audio session refresh error: {ex.Message}");
    }

    private static int CountAppGroupChanges(
        IReadOnlyList<AudioAppGroup> oldGroups,
        IReadOnlyList<AudioAppGroup> newGroups)
    {
        var oldSignatures = oldGroups.ToDictionary(
            group => group.ProcessName,
            GetAppGroupSignature,
            StringComparer.OrdinalIgnoreCase);
        var newSignatures = newGroups.ToDictionary(
            group => group.ProcessName,
            GetAppGroupSignature,
            StringComparer.OrdinalIgnoreCase);

        var changes = newSignatures.Count(group =>
            !oldSignatures.TryGetValue(group.Key, out var oldSignature) ||
            !oldSignature.Equals(group.Value, StringComparison.Ordinal));
        changes += oldSignatures.Keys.Count(processName => !newSignatures.ContainsKey(processName));
        return changes;
    }

    private static string GetAppGroupSignature(AudioAppGroup group)
    {
        return string.Join(
            "|",
            group.AssignedChannel,
            group.TargetEndpointId ?? string.Empty,
            group.Status,
            group.SessionCount.ToString(CultureInfo.InvariantCulture),
            FormatProcessIds(group.ProcessIds),
            group.CurrentEndpointsSummary);
    }

    private async Task HandleAutoApplyTimerTickAsync()
    {
        if (!settings.AutoApplyRoutingRules)
        {
            return;
        }

        var refreshed = await RefreshAppSessionsAsync(
            logDetails: false,
            autoTriggered: true,
            force: false,
            bindGrid: false);
        if (!refreshed)
        {
            return;
        }

        await AutoApplyPendingRoutesAsync();
    }

    private void ApplyRouting(bool autoTriggered)
    {
        if (isApplyingRouting)
        {
            LogRoutingMessage(
                autoTriggered ? "auto-apply-skipped-running" : "manual-apply-skipped-running",
                autoTriggered ? "Auto apply skipped: routing already running." : "Apply Routing skipped: routing already running.",
                autoTriggered);
            return;
        }

        isApplyingRouting = true;
        try
        {
        if (autoTriggered)
        {
            RefreshAppSessions(logDetails: false);
        }

        SaveRoutingRulesFromGrid(persist: false);
        var processed = false;
        var successfulRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow row in appSessionsGrid.Rows)
        {
            processed |= row.Tag switch
            {
                AudioAppGroup group => ApplyRoutingToGroup(row, group, successfulRoutes, autoTriggered),
                AudioAppSession session => ApplyRoutingToSession(row, session, successfulRoutes, autoTriggered),
                _ => false
            };
        }

        if (!processed && !autoTriggered)
        {
            AppendLog("No assigned application routing rules to apply.");
        }

        SaveSettings();
        RefreshAppSessions(logDetails: !autoTriggered);
        ApplyPostRoutingStatuses(successfulRoutes);
        LogPostApplyRoutingState(autoTriggered, successfulRoutes);
        }
        finally
        {
            isApplyingRouting = false;
        }
    }

    private bool ApplyRoutingToGroup(
        DataGridViewRow row,
        AudioAppGroup group,
        ISet<string> successfulRoutes,
        bool autoTriggered)
    {
        var assignedChannel = row.Cells["AssignedChannel"].Value?.ToString() ?? "None";
        if (assignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            SetRoutingRowStatus(row, "Active");
            return false;
        }

        var target = ResolveTargetEndpoint(assignedChannel, GetRoutingRuleForProcess(group.ProcessName));
        if (target is null)
        {
            SetRoutingRowStatus(row, "Error");
            LogRoutingMessage(
                $"missing-target:{group.ProcessName}:{assignedChannel}",
                $"{group.ProcessName} has no target endpoint for {assignedChannel}. Select the channel endpoint first.",
                autoTriggered);
            return true;
        }

        group.TargetEndpointId = target.Id;
        group.TargetEndpointFriendlyName = target.FriendlyName;
        row.Cells["TargetEndpoint"].Value = target.FriendlyName;

        if (group.AreAllSessionsOnTarget())
        {
            SetRoutingRowStatus(row, "Already routed");
            LogRoutingMessage(
                $"already-group:{group.ProcessName}:{target.Id}",
                $"{group.ProcessName} is already routed to {target.FriendlyName}.",
                autoTriggered);
            return true;
        }

        if (!settings.EnableExperimentalAutomaticRouting)
        {
            SetRoutingRowStatus(row, group.HasAnySessionOnTarget() ? "Partially routed" : "Manual required");
            LogManualRoutingRequired(group.ProcessName, target.FriendlyName, autoTriggered);
            return true;
        }

        LogRoutingGroupAttempt(group, assignedChannel, target, autoTriggered);

        var successCount = 0;
        var lastStatus = "Error";
        foreach (var processId in group.ProcessIds)
        {
            var result = audioPolicyRouter.TrySetAppOutputDevice(
                (uint)processId,
                group.ProcessName,
                target.Id,
                target.FriendlyName);

            lastStatus = result.Success ? "Routed preference saved" : result.Status;
            if (result.Success)
            {
                successCount++;
                successfulRoutes.Add(GetRouteKey(group.ProcessName, target.Id));
            }

            LogRoutingMessage(
                $"route-group-pid:{group.ProcessName}:{processId}:{target.Id}:{lastStatus}",
                $"PID {processId}: {(result.Success ? "preference saved" : result.Status)}",
                autoTriggered);
            LogRoutingMessage(
                $"route:{group.ProcessName}:{processId}:{target.Id}:{lastStatus}",
                result.Message,
                autoTriggered);
            LogRoutingResult(result, autoTriggered);
        }

        var status = successCount > 0 ? "Routed preference saved" : lastStatus;
        SetRoutingRowStatus(row, status);
        LogRoutingMessage(
            $"route-group-result:{group.ProcessName}:{target.Id}:{status}",
            $"Group result: {status}",
            autoTriggered);
        return true;
    }

    private bool ApplyRoutingToSession(
        DataGridViewRow row,
        AudioAppSession session,
        ISet<string> successfulRoutes,
        bool autoTriggered)
    {
        var assignedChannel = row.Cells["AssignedChannel"].Value?.ToString() ?? "None";
        if (assignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            SetRoutingRowStatus(row, "Active");
            return false;
        }

        var target = ResolveTargetEndpoint(assignedChannel, GetRoutingRuleForProcess(session.ProcessName));
        if (target is null)
        {
            SetRoutingRowStatus(row, "Error");
            LogRoutingMessage(
                $"missing-target:{session.ProcessName}:{assignedChannel}",
                $"{session.ProcessName} has no target endpoint for {assignedChannel}. Select the channel endpoint first.",
                autoTriggered);
            return true;
        }

        row.Cells["TargetEndpoint"].Value = target.FriendlyName;

        if (session.CurrentEndpointId.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
        {
            SetRoutingRowStatus(row, "Already routed");
            LogRoutingMessage(
                $"already:{session.ProcessName}:{target.Id}",
                $"{session.ProcessName} PID {session.ProcessId} is already routed to {target.FriendlyName}.",
                autoTriggered);
            return true;
        }

        if (!settings.EnableExperimentalAutomaticRouting)
        {
            SetRoutingRowStatus(row, "Manual required");
            LogManualRoutingRequired(session.ProcessName, target.FriendlyName, autoTriggered);
            return true;
        }

        LogRoutingAttempt(session, assignedChannel, target, autoTriggered);

        var result = audioPolicyRouter.TrySetAppOutputDevice(
            (uint)session.ProcessId,
            session.ProcessName,
            target.Id,
            target.FriendlyName);

        var status = result.Success ? "Routed preference saved" : result.Status;
        if (result.Success)
        {
            successfulRoutes.Add(GetRouteKey(session.ProcessName, target.Id));
        }

        SetRoutingRowStatus(row, status);
        LogRoutingMessage(
            $"route:{session.ProcessName}:{session.ProcessId}:{target.Id}:{status}",
            result.Message,
            autoTriggered);
        LogRoutingResult(result, autoTriggered);
        return true;
    }

    private void ClearRoutingRules()
    {
        settings.RoutingRules.Clear();
        settings.RoutingRulesInitialized = true;

        foreach (DataGridViewRow row in appSessionsGrid.Rows)
        {
            row.Cells["AssignedChannel"].Value = "None";
            row.Cells["TargetEndpoint"].Value = string.Empty;
            SetRoutingRowStatus(row, "Active");
        }

        AssignChannelsToSessions();
        appGroups = BuildAppGroups();
        UpdateChannelControlAvailability();
        SaveSettings();
        AppendLog("Application routing rules cleared.");
    }

    private void OpenWindowsVolumeMixer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true });
            AppendLog("Opened Windows Volume Mixer settings.");
        }
        catch (Exception ex)
        {
            AppendLog($"Could not open apps volume settings: {ex.Message}");
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
                AppendLog("Opened Windows Sound settings.");
            }
            catch (Exception fallbackEx)
            {
                AppendLog($"Could not open Windows Sound settings: {fallbackEx.Message}");
            }
        }
    }

    private void SetRoutingRowStatus(DataGridViewRow row, string status)
    {
        row.Cells["Status"].Value = status;
        if (row.Tag is AudioAppSession session)
        {
            session.Status = status;
        }
        else if (row.Tag is AudioAppGroup group)
        {
            group.Status = status;
        }

        row.DefaultCellStyle.ForeColor = status switch
        {
            "Already routed" => Color.ForestGreen,
            "Partially routed" => Color.RoyalBlue,
            "Routed" => Color.ForestGreen,
            "Routed preference saved" => Color.RoyalBlue,
            "Manual required" => Color.DarkOrange,
            "Restart app may be required" => Color.DarkOrange,
            "Restart app required" => Color.DarkOrange,
            "Verification failed" => Color.Firebrick,
            "Experimental API error" => Color.Firebrick,
            "Error" => Color.Firebrick,
            _ => Color.Gainsboro
        };
    }

    private void LogRoutingAttempt(AudioAppSession session, string assignedChannel, AudioEndpoint target, bool autoTriggered)
    {
        var lines = new[]
        {
            $"Routing {session.ProcessName} PID {session.ProcessId}:",
            $"current endpoint = {session.CurrentEndpointFriendlyName}",
            $"target channel = {assignedChannel}",
            $"target endpoint = {target.FriendlyName}",
            $"target endpoint ID = {target.Id}",
            $"experimental = {settings.EnableExperimentalAutomaticRouting}"
        };

        for (var index = 0; index < lines.Length; index++)
        {
            LogRoutingMessage(
                $"routing-attempt:{session.ProcessName}:{session.ProcessId}:{target.Id}:{index}",
                lines[index],
                autoTriggered);
        }
    }

    private void LogRoutingGroupAttempt(AudioAppGroup group, string assignedChannel, AudioEndpoint target, bool autoTriggered)
    {
        var lines = new[]
        {
            $"Routing group {group.ProcessName} -> {assignedChannel} / {target.FriendlyName}",
            $"PIDs = {FormatProcessIds(group.ProcessIds)}",
            $"sessions = {group.SessionCount}",
            $"current endpoints = {group.CurrentEndpointsSummary}",
            $"target endpoint ID = {target.Id}",
            $"experimental = {settings.EnableExperimentalAutomaticRouting}"
        };

        for (var index = 0; index < lines.Length; index++)
        {
            LogRoutingMessage(
                $"routing-group-attempt:{group.ProcessName}:{target.Id}:{index}",
                lines[index],
                autoTriggered);
        }
    }

    private void LogRoutingResult(AppOutputRoutingResult result, bool autoTriggered)
    {
        for (var index = 0; index < result.DiagnosticMessages.Count; index++)
        {
            LogRoutingMessage(
                $"routing-diagnostic:{result.ProcessName}:{result.ProcessId}:{result.TargetEndpointId}:{index}:{result.DiagnosticMessages[index]}",
                result.DiagnosticMessages[index],
                autoTriggered);
        }

        if (!string.IsNullOrWhiteSpace(result.ExceptionMessage))
        {
            LogRoutingMessage(
                $"routing-exception:{result.ProcessName}:{result.MethodName}:{result.Role}:{result.HResult}",
                $"{result.MethodName} failed: HRESULT = {result.HResult}; Exception = {result.ExceptionType}: {result.ExceptionMessage}; Interface implementation = {result.InterfaceImplementation}",
                autoTriggered);
        }

        if (!string.IsNullOrWhiteSpace(result.VerificationEndpointId))
        {
            LogRoutingMessage(
                $"routing-verification:{result.ProcessName}:{result.Role}:{result.VerificationEndpointId}",
                $"GetPersistedDefaultAudioEndpoint returned = {result.VerificationEndpointId}",
                autoTriggered);
        }
    }

    private void LogManualRoutingRequired(string processName, string targetEndpointFriendlyName, bool autoTriggered)
    {
        LogRoutingMessage(
            $"manual:{processName}:{targetEndpointFriendlyName}",
            $"{processName} should be routed to {targetEndpointFriendlyName}. Open Windows Volume Mixer and set Output device manually.",
            autoTriggered);
    }

    private void LogRoutingMessage(string key, string message, bool autoTriggered)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (autoTriggered &&
            lastAutoRoutingMessages.TryGetValue(key, out var previousMessage) &&
            previousMessage.Equals(message, StringComparison.Ordinal))
        {
            return;
        }

        if (autoTriggered)
        {
            lastAutoRoutingMessages[key] = message;
        }

        AppendLog(message);
    }

    private AppRoutingRule? GetRoutingRuleForProcess(string processName)
    {
        return settings.RoutingRules.FirstOrDefault(rule =>
            rule.Enabled &&
            NormalizeProcessName(rule.ProcessName).Equals(NormalizeProcessName(processName), StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyPostRoutingStatuses(IReadOnlySet<string> successfulRoutes)
    {
        foreach (DataGridViewRow row in appSessionsGrid.Rows)
        {
            if (row.Tag is AudioAppGroup group)
            {
                if (string.IsNullOrWhiteSpace(group.TargetEndpointId) ||
                    !successfulRoutes.Contains(GetRouteKey(group.ProcessName, group.TargetEndpointId)))
                {
                    continue;
                }

                SetRoutingRowStatus(row, GetPostApplyGroupStatus(group));
                continue;
            }

            if (row.Tag is AudioAppSession session &&
                !string.IsNullOrWhiteSpace(session.TargetEndpointId) &&
                successfulRoutes.Contains(GetRouteKey(session.ProcessName, session.TargetEndpointId)))
            {
                SetRoutingRowStatus(
                    row,
                    session.CurrentEndpointId.Equals(session.TargetEndpointId, StringComparison.OrdinalIgnoreCase)
                        ? "Already routed"
                        : "Restart app required");
            }
        }
    }

    private void LogPostApplyRoutingState(bool autoTriggered, IReadOnlySet<string> successfulRoutes)
    {
        foreach (var group in appGroups)
        {
            if (group.AssignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(group.TargetEndpointId) ||
                group.AreAllSessionsOnTarget())
            {
                continue;
            }

            var routeKey = GetRouteKey(group.ProcessName, group.TargetEndpointId);
            LogRoutingMessage(
                $"still-not-routed:{routeKey}",
                successfulRoutes.Contains(routeKey)
                    ? $"{group.ProcessName}: rule saved; restart app or restart playback may be required."
                    : $"{group.ProcessName}: rule saved, but app is still on another endpoint. Restart app or set output manually in Windows Volume Mixer.",
                autoTriggered);
        }
    }

    private static string GetRouteKey(string processName, string endpointId)
        => $"{NormalizeProcessName(processName)}|{endpointId}";

    private void AssignChannelsToSessions()
    {
        var rules = GetRoutingRuleMap();
        foreach (var session in appSessions)
        {
            var normalizedProcessName = NormalizeProcessName(session.ProcessName);
            if (rules.TryGetValue(normalizedProcessName, out var rule))
            {
                session.AssignedChannel = rule.PreferredChannel;
                var target = ResolveTargetEndpoint(rule.PreferredChannel, rule);
                session.TargetEndpointId = target?.Id;
                session.TargetEndpointFriendlyName = target?.FriendlyName ?? rule.PreferredEndpointFriendlyName ?? string.Empty;
                session.Status = GetRoutingStatus(session);
            }
            else
            {
                session.AssignedChannel = "None";
                session.TargetEndpointId = null;
                session.TargetEndpointFriendlyName = string.Empty;
                session.Status = "Active";
            }
        }
    }

    private IReadOnlyList<AudioAppGroup> BuildAppGroups()
    {
        var rules = GetRoutingRuleMap();
        return appSessions
            .GroupBy(session => NormalizeProcessName(session.ProcessName), StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateAppGroup(group.Key, group.ToList(), rules))
            .OrderBy(group => group.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private AudioAppGroup CreateAppGroup(
        string processName,
        List<AudioAppSession> sessions,
        IReadOnlyDictionary<string, AppRoutingRule> rules)
    {
        rules.TryGetValue(processName, out var rule);
        var assignedChannel = rule?.PreferredChannel ?? "None";
        var target = rule is null ? null : ResolveTargetEndpoint(assignedChannel, rule);
        var group = new AudioAppGroup
        {
            ProcessName = processName,
            DisplayName = sessions.FirstOrDefault()?.DisplayName ?? processName,
            Sessions = sessions,
            ProcessIds = sessions
                .Select(session => session.ProcessId)
                .Distinct()
                .OrderBy(processId => processId)
                .ToList(),
            AssignedChannel = assignedChannel,
            TargetEndpointId = target?.Id,
            TargetEndpointFriendlyName = target?.FriendlyName ?? rule?.PreferredEndpointFriendlyName ?? string.Empty,
            Volume = sessions.Count == 0 ? 0f : sessions.Average(session => session.Volume),
            CurrentEndpointsSummary = FormatEndpointSummary(sessions)
        };

        group.Status = GetRoutingStatus(group);
        return group;
    }

    private void BindAppSessionsGrid()
    {
        updatingAppRoutingUi = true;
        appSessionsGrid.Rows.Clear();

        if (showRawSessionsCheckBox.Checked)
        {
            foreach (var session in appSessions)
            {
                var rowIndex = appSessionsGrid.Rows.Add(
                    session.ProcessName,
                    session.ProcessId > 0 ? session.ProcessId.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    1,
                    session.CurrentEndpointFriendlyName,
                    session.AssignedChannel,
                    session.TargetEndpointFriendlyName,
                    $"{session.VolumePercent}%",
                    session.Status);

                appSessionsGrid.Rows[rowIndex].Tag = session;
            }
        }
        else
        {
            foreach (var group in appGroups)
            {
                var rowIndex = appSessionsGrid.Rows.Add(
                    group.ProcessName,
                    FormatProcessIds(group.ProcessIds),
                    group.SessionCount,
                    group.CurrentEndpointsSummary,
                    group.AssignedChannel,
                    group.TargetEndpointFriendlyName,
                    $"{group.VolumePercent}%",
                    group.Status);

                appSessionsGrid.Rows[rowIndex].Tag = group;
            }
        }

        updatingAppRoutingUi = false;
    }

    private void SaveRoutingRulesFromGrid(bool persist)
    {
        if (updatingAppRoutingUi)
        {
            return;
        }

        var rules = GetRoutingRuleMap();

        foreach (DataGridViewRow row in appSessionsGrid.Rows)
        {
            var processName = row.Tag switch
            {
                AudioAppGroup group => group.ProcessName,
                AudioAppSession session => session.ProcessName,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            var selectedChannel = row.Cells["AssignedChannel"].Value?.ToString() ?? "None";
            if (selectedChannel.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                !MixerChannel.DefaultChannelNames.Contains(selectedChannel, StringComparer.OrdinalIgnoreCase))
            {
                rules.Remove(NormalizeProcessName(processName));
                continue;
            }

            var target = ResolveTargetEndpoint(selectedChannel, null);
            var normalizedProcessName = NormalizeProcessName(processName);
            rules[normalizedProcessName] = new AppRoutingRule
            {
                ProcessName = normalizedProcessName,
                PreferredChannel = selectedChannel,
                PreferredEndpointId = target?.Id,
                PreferredEndpointFriendlyName = target?.FriendlyName,
                Enabled = true
            };
        }

        StoreRoutingRuleMap(rules);

        AssignChannelsToSessions();
        appGroups = BuildAppGroups();
        UpdateMixerAppSummaries();

        if (persist)
        {
            SaveSettings();
            AppendLog("Application routing rules saved.");
        }
    }

    private void UpdateChannelControlAvailability()
    {
        foreach (var controls in channelControlsByName.Values)
        {
            SetEndpointControlsEnabled(controls, enabled: true);
        }

        UpdateMixerAppSummaries();
    }

    private void UpdateMixerAppSummaries()
    {
        UpdateMasterStripSummary();

        foreach (var controls in channelControlsByName.Values)
        {
            var assignedGroups = appGroups
                .Where(group => group.AssignedChannel.Equals(controls.Channel.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(group => group.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var signature = BuildAppChipSignature(assignedGroups);
            if (channelAppChipSignatures.TryGetValue(controls.Channel.Name, out var previousSignature) &&
                previousSignature.Equals(signature, StringComparison.Ordinal))
            {
                continue;
            }

            channelAppChipSignatures[controls.Channel.Name] = signature;
            var visibleGroups = assignedGroups.Take(4).ToList();
            var chips = visibleGroups
                .Select(group => CreateAppChip(group, controls.Channel.Name))
                .Cast<Control>()
                .ToList();
            controls.StripControl.SetAppChips(chips, assignedGroups.Count - visibleGroups.Count);
            RegisterChannelDropTargetForChildren(controls.StripControl.AppChipsPanel, controls.Channel.Name);
        }

        UpdateUnassignedAppChips();
    }

    private void UpdateUnassignedAppChips()
    {
        if (unassignedAppsFlowPanel is null)
        {
            return;
        }

        var unassignedGroups = appGroups
            .Where(group => group.AssignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase))
            .Where(group => !ShouldHideFromUnassignedMixer(group))
            .OrderBy(group => group.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var signature = BuildAppChipSignature(unassignedGroups);
        if (unassignedAppChipSignature.Equals(signature, StringComparison.Ordinal))
        {
            return;
        }

        unassignedAppChipSignature = signature;
        unassignedAppsFlowPanel.SuspendLayout();
        var oldControls = unassignedAppsFlowPanel.Controls.Cast<Control>().ToList();
        unassignedAppsFlowPanel.Controls.Clear();
        foreach (var control in oldControls)
        {
            control.Dispose();
        }

        if (unassignedGroups.Count == 0)
        {
            var placeholder = CreateMixerPlaceholderLabel("No apps");
            RegisterUnassignedDropTarget(placeholder);
            unassignedAppsFlowPanel.Controls.Add(placeholder);
        }
        else
        {
            foreach (var group in unassignedGroups)
            {
                unassignedAppsFlowPanel.Controls.Add(CreateAppChip(group, channelDropTarget: null));
            }
        }

        unassignedAppsFlowPanel.ResumeLayout();
    }

    private AppChipControl CreateAppChip(AudioAppGroup group, string? channelDropTarget)
    {
        var chip = new AppChipControl(group.ProcessName, GetAppChipStatus(group));
        if (channelDropTarget is null)
        {
            RegisterUnassignedDropTarget(chip);
            RegisterUnassignedDropTargetForChildren(chip);
        }

        return chip;
    }

    private static string BuildAppChipSignature(IEnumerable<AudioAppGroup> groups)
    {
        return string.Join(
            "||",
            groups.Select(group => string.Join(
                "|",
                group.ProcessName,
                group.AssignedChannel,
                group.TargetEndpointId ?? string.Empty,
                group.CurrentEndpointsSummary,
                GetAppChipStatus(group).ToString())));
    }

    private void RegisterUnassignedDropTargetForChildren(Control control)
    {
        foreach (Control child in control.Controls)
        {
            RegisterUnassignedDropTarget(child);
            RegisterUnassignedDropTargetForChildren(child);
        }
    }

    private static Label CreateMixerPlaceholderLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Margin = new Padding(6, 7, 4, 4),
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(170, 176, 188),
            Text = text
        };
    }

    private static bool ShouldHideFromUnassignedMixer(AudioAppGroup group)
    {
        return group.ProcessName.Equals("System Sounds", StringComparison.OrdinalIgnoreCase) ||
            group.DisplayName.Equals("System Sounds", StringComparison.OrdinalIgnoreCase);
    }

    private static AppChipStatus GetAppChipStatus(AudioAppGroup group)
    {
        if (group.AssignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return AppChipStatus.Neutral;
        }

        if (group.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(group.TargetEndpointId))
        {
            return AppChipStatus.Error;
        }

        return group.AreAllSessionsOnTarget() ||
            group.Status.Contains("Routed", StringComparison.OrdinalIgnoreCase) ||
            group.Status.Contains("Already", StringComparison.OrdinalIgnoreCase)
                ? AppChipStatus.Routed
                : AppChipStatus.Pending;
    }

    private void AssignAppFromMixer(string processName, string? channelName)
    {
        var normalizedProcessName = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return;
        }

        var rules = GetRoutingRuleMap();
        if (string.IsNullOrWhiteSpace(channelName) ||
            channelName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            rules.Remove(normalizedProcessName);
            AppendLog($"Unassigned {normalizedProcessName}");
        }
        else
        {
            var channel = MixerChannel.DefaultChannelNames.FirstOrDefault(name =>
                name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(channel))
            {
                AppendLog($"Cannot assign {normalizedProcessName}: unknown channel {channelName}.");
                return;
            }

            var target = ResolveTargetEndpoint(channel, null);
            rules[normalizedProcessName] = new AppRoutingRule
            {
                ProcessName = normalizedProcessName,
                PreferredChannel = channel,
                PreferredEndpointId = target?.Id,
                PreferredEndpointFriendlyName = target?.FriendlyName,
                Enabled = true
            };
            AppendLog($"Assigned {normalizedProcessName} to {channel}");
        }

        StoreRoutingRuleMap(rules);
        AssignChannelsToSessions();
        appGroups = BuildAppGroups();
        BindAppSessionsGrid();
        UpdateChannelControlAvailability();
        SaveSettings();

        if (!string.IsNullOrWhiteSpace(channelName) &&
            !channelName.Equals("None", StringComparison.OrdinalIgnoreCase) &&
            settings.AutoApplyRoutingRules)
        {
            _ = AutoApplyPendingRoutesAsync(normalizedProcessName);
        }
    }

    private async Task AutoApplyPendingRoutesAsync(string? onlyProcessName = null)
    {
        if (isApplyingRouting)
        {
            LogRoutingMessage("auto-apply-skipped-running", "Auto apply skipped: routing already running.", autoTriggered: true);
            return;
        }

        var requests = BuildAutoRoutingRequests(onlyProcessName);
        if (requests.Count == 0)
        {
            return;
        }

        isApplyingRouting = true;
        try
        {
            var outcomes = await Task.Run(() =>
                requests
                    .Select(request => new AutoRoutingOutcome(
                        request,
                        request.ProcessIds
                            .Select(processId => audioPolicyRouter.TrySetAppOutputDevice(
                                (uint)processId,
                                request.ProcessName,
                                request.TargetEndpointId,
                                request.TargetEndpointFriendlyName))
                            .ToList()))
                    .ToList());

            if (IsDisposed || Disposing)
            {
                return;
            }

            foreach (var outcome in outcomes)
            {
                var routeKey = GetRouteKey(outcome.Request.ProcessName, outcome.Request.TargetEndpointId);
                var success = outcome.Results.Any(result => result.Success);
                var status = success
                    ? "Routed preference saved"
                    : outcome.Results.LastOrDefault()?.Status ?? "Error";
                lastAutoRoutingAttempts[routeKey] = new AutoRoutingAttempt(
                    DateTime.UtcNow,
                    status,
                    string.Join(",", outcome.Request.ProcessIds));

                foreach (var result in outcome.Results)
                {
                    LogRoutingResult(result, autoTriggered: true);
                }

                var group = appGroups.FirstOrDefault(group =>
                    group.ProcessName.Equals(outcome.Request.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (group is not null)
                {
                    group.Status = status;
                }

                AppendLog(success
                    ? $"Auto apply routed {outcome.Request.ProcessName} -> {outcome.Request.AssignedChannel}"
                    : $"Auto apply {outcome.Request.ProcessName} -> {outcome.Request.AssignedChannel}: {status}");
            }

            if (mainTabControl.SelectedTab == routingTabPage)
            {
                BindAppSessionsGrid();
            }

            UpdateMixerAppSummaries();
        }
        catch (Exception ex)
        {
            AppendLog($"Auto apply error: {ex.Message}");
        }
        finally
        {
            isApplyingRouting = false;
        }
    }

    private List<AutoRoutingRequest> BuildAutoRoutingRequests(string? onlyProcessName)
    {
        var now = DateTime.UtcNow;
        var requests = new List<AutoRoutingRequest>();
        foreach (var group in appGroups)
        {
            if (!string.IsNullOrWhiteSpace(onlyProcessName) &&
                !NormalizeProcessName(group.ProcessName).Equals(
                    NormalizeProcessName(onlyProcessName),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (group.AssignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(group.TargetEndpointId) ||
                group.ProcessIds.Count == 0)
            {
                continue;
            }

            var routeKey = GetRouteKey(group.ProcessName, group.TargetEndpointId);
            if (group.AreAllSessionsOnTarget())
            {
                LogRoutingMessage(
                    $"auto-apply-already-routed:{routeKey}",
                    $"Auto apply skipped: already routed {group.ProcessName}.",
                    autoTriggered: true);
                continue;
            }

            if (!settings.EnableExperimentalAutomaticRouting)
            {
                LogManualRoutingRequired(group.ProcessName, group.TargetEndpointFriendlyName, autoTriggered: true);
                continue;
            }

            var routablePids = group.ProcessIds.Where(pid => pid > 0).OrderBy(pid => pid).ToList();
            if (routablePids.Count == 0)
            {
                LogRoutingMessage(
                    $"auto-apply-unroutable:{routeKey}",
                    $"Auto apply skipped: {group.ProcessName} has no routable process.",
                    autoTriggered: true);
                continue;
            }

            var pidSignature = string.Join(",", routablePids);
            if (lastAutoRoutingAttempts.TryGetValue(routeKey, out var lastAttempt))
            {
                // Re-routing an app whose preference is already saved only helps once
                // its process set changes (restart); repeating the policy write every
                // cycle makes audiosrv re-evaluate live streams and glitches audio.
                if (lastAttempt.Status.Equals("Routed preference saved", StringComparison.OrdinalIgnoreCase) &&
                    lastAttempt.PidSignature == pidSignature)
                {
                    LogRoutingMessage(
                        $"auto-apply-saved:{routeKey}",
                        $"Auto apply skipped: routing preference already saved for {group.ProcessName}.",
                        autoTriggered: true);
                    continue;
                }

                if (now - lastAttempt.Utc < autoRoutingCooldown)
                {
                    LogRoutingMessage(
                        $"auto-apply-recent:{routeKey}",
                        $"Auto apply skipped: recently applied {group.ProcessName}.",
                        autoTriggered: true);
                    continue;
                }
            }

            lastAutoRoutingAttempts[routeKey] = new AutoRoutingAttempt(now, "Started", pidSignature);
            requests.Add(new AutoRoutingRequest(
                group.ProcessName,
                group.AssignedChannel,
                group.TargetEndpointId,
                group.TargetEndpointFriendlyName,
                routablePids));
        }

        return requests;
    }

    private void StoreRoutingRuleMap(IDictionary<string, AppRoutingRule> rules)
    {
        settings.RoutingRules = rules
            .OrderBy(rule => rule.Key, StringComparer.OrdinalIgnoreCase)
            .Select(rule =>
            {
                rule.Value.ProcessName = rule.Key;
                return rule.Value;
            })
            .ToList();
    }

    private Dictionary<string, AppRoutingRule> GetRoutingRuleMap()
    {
        return settings.RoutingRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ProcessName) &&
                rule.Enabled &&
                MixerChannel.DefaultChannelNames.Contains(rule.PreferredChannel, StringComparer.OrdinalIgnoreCase))
            .GroupBy(rule => NormalizeProcessName(rule.ProcessName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    first.ProcessName = group.Key;
                    first.PreferredChannel = MixerChannel.DefaultChannelNames.First(channel =>
                        channel.Equals(first.PreferredChannel, StringComparison.OrdinalIgnoreCase));
                    return first;
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private AudioEndpoint? ResolveTargetEndpoint(string channelName, AppRoutingRule? rule)
    {
        if (channelControlsByName.TryGetValue(channelName, out var controls))
        {
            var selectedEndpointId = controls.Channel.SelectedEndpointId;
            if (!string.IsNullOrWhiteSpace(selectedEndpointId))
            {
                var selectedEndpoint = endpoints.FirstOrDefault(endpoint =>
                    endpoint.Id.Equals(selectedEndpointId, StringComparison.OrdinalIgnoreCase));
                if (selectedEndpoint is not null)
                {
                    return selectedEndpoint;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rule?.PreferredEndpointId))
        {
            var savedEndpoint = endpoints.FirstOrDefault(endpoint =>
                endpoint.Id.Equals(rule.PreferredEndpointId, StringComparison.OrdinalIgnoreCase));
            if (savedEndpoint is not null)
            {
                return savedEndpoint;
            }
        }

        return null;
    }

    private static string GetRoutingStatus(AudioAppSession session)
    {
        if (session.AssignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }

        if (string.IsNullOrWhiteSpace(session.TargetEndpointId))
        {
            return "Error";
        }

        return session.CurrentEndpointId.Equals(session.TargetEndpointId, StringComparison.OrdinalIgnoreCase)
            ? "Already routed"
            : "Manual required";
    }

    private static string GetRoutingStatus(AudioAppGroup group)
    {
        if (group.AssignedChannel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }

        if (string.IsNullOrWhiteSpace(group.TargetEndpointId))
        {
            return "Error";
        }

        if (group.AreAllSessionsOnTarget())
        {
            return "Already routed";
        }

        return group.HasAnySessionOnTarget() ? "Partially routed" : "Manual required";
    }

    private static string GetPostApplyGroupStatus(AudioAppGroup group)
    {
        if (group.AreAllSessionsOnTarget())
        {
            return "Already routed";
        }

        return group.HasAnySessionOnTarget() ? "Partially routed" : "Restart app required";
    }

    private static string FormatEndpointSummary(IEnumerable<AudioAppSession> sessions)
    {
        var endpointNames = sessions
            .Select(session => session.CurrentEndpointFriendlyName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return endpointNames.Count == 0 ? "Unknown" : string.Join(", ", endpointNames);
    }

    private static string FormatProcessIds(IEnumerable<int> processIds)
    {
        var formattedProcessIds = processIds
            .Where(processId => processId > 0)
            .Distinct()
            .OrderBy(processId => processId)
            .Select(processId => processId.ToString(CultureInfo.InvariantCulture))
            .ToList();

        return formattedProcessIds.Count == 0 ? "System" : string.Join(", ", formattedProcessIds);
    }

    private void LogSessionVolumeResults(
        string channelName,
        IReadOnlyCollection<string> processNames,
        int volumePercent,
        IReadOnlyList<AudioSessionActionResult> results)
    {
        if (results.Count == 0)
        {
            AppendLog($"{channelName}: no active audio sessions matched {string.Join(", ", processNames)}; using endpoint fallback.");
            return;
        }

        foreach (var result in results)
        {
            if (result.Status == "Applied")
            {
                AppendLog($"{channelName}: {result.ProcessName} session volume = {volumePercent}%");
            }
            else
            {
                AppendLog($"{channelName}: {result.ProcessName} session volume error: {result.ErrorMessage}");
            }
        }
    }

    private void LogSessionMuteResults(
        string channelName,
        IReadOnlyCollection<string> processNames,
        bool muted,
        IReadOnlyList<AudioSessionActionResult> results)
    {
        if (results.Count == 0)
        {
            AppendLog($"{channelName}: no active audio sessions matched {string.Join(", ", processNames)}; using endpoint fallback.");
            return;
        }

        foreach (var result in results)
        {
            if (result.Status == "Applied")
            {
                AppendLog($"{channelName}: {result.ProcessName} session mute = {(muted ? "on" : "off")}");
            }
            else
            {
                AppendLog($"{channelName}: {result.ProcessName} session mute error: {result.ErrorMessage}");
            }
        }
    }

    private bool IsApplicationSessionMode()
    {
        return settings.ChannelVolumeMode.Equals("ApplicationSessions", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldApplyAppSessionVolume()
    {
        return false;
    }

    private bool ShouldApplyMonitorMixGain()
    {
        return true;
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Equals("System Sounds", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("pid:", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".exe";
    }

    private void LoadSerialSettings()
    {
        updatingSerialUi = true;
        baudRateTextBox.Text = settings.SerialBaudRate.ToString(CultureInfo.InvariantCulture);
        updatingSerialUi = false;
    }

    private void RefreshComPorts()
    {
        updatingSerialUi = true;

        var selectedPort = settings.SelectedComPort;
        var ports = SerialController.GetPortNames().ToList();
        if (!string.IsNullOrWhiteSpace(selectedPort) &&
            !ports.Contains(selectedPort, StringComparer.OrdinalIgnoreCase))
        {
            ports.Add(selectedPort);
        }

        serialPortComboBox.Items.Clear();
        foreach (var port in ports.OrderBy(port => port, StringComparer.OrdinalIgnoreCase))
        {
            serialPortComboBox.Items.Add(port);
        }

        serialPortComboBox.SelectedItem = !string.IsNullOrWhiteSpace(selectedPort) &&
            serialPortComboBox.Items.Contains(selectedPort)
                ? selectedPort
                : null;

        updatingSerialUi = false;
        AppendLog($"Found {ports.Count} COM port(s).");
    }

    private void ToggleSerialConnection()
    {
        if (serialController.IsConnected)
        {
            serialController.Disconnect();
            serialStatusValueLabel.Text = "Disconnected";
            connectSerialButton.Text = "Connect";
            AppendLog("Serial disconnected.");
            return;
        }

        if (serialPortComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            AppendLog("Select a COM port before connecting.");
            return;
        }

        if (!TryReadBaudRate(out var baudRate))
        {
            AppendLog("Baud rate must be a positive integer.");
            return;
        }

        try
        {
            serialController.Connect(portName, baudRate);
            settings.SelectedComPort = portName;
            settings.SerialBaudRate = baudRate;
            SaveSettings();

            serialStatusValueLabel.Text = $"Connected to {portName} @ {baudRate}";
            connectSerialButton.Text = "Disconnect";
            AppendLog($"Serial connected: {portName} @ {baudRate}.");
        }
        catch (Exception ex)
        {
            serialStatusValueLabel.Text = "Error";
            AppendLog($"Serial connect error: {ex.Message}");
        }
    }

    private void SaveSerialSelection()
    {
        if (updatingSerialUi)
        {
            return;
        }

        settings.SelectedComPort = serialPortComboBox.SelectedItem as string;
        SaveSettings();
    }

    private void SaveBaudRateIfValid()
    {
        if (updatingSerialUi)
        {
            return;
        }

        if (TryReadBaudRate(out var baudRate))
        {
            settings.SerialBaudRate = baudRate;
            SaveSettings();
        }
    }

    private bool TryReadBaudRate(out int baudRate)
    {
        return int.TryParse(
                baudRateTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out baudRate) &&
            baudRate > 0;
    }

    private void HandleSerialCommand(SerialCommandReceivedEventArgs args)
    {
        AppendLog($"Serial command: {args.Line}");

        if (!channelControlsByName.TryGetValue(args.Command.ChannelName, out var controls))
        {
            AppendLog($"Serial command ignored: unknown channel {args.Command.ChannelName}.");
            return;
        }

        switch (args.Command.Type)
        {
            case SerialCommandType.SetVolume when args.Command.VolumePercent is int volumePercent:
                ApplyChannelVolume(controls, volumePercent);
                break;
            case SerialCommandType.SetMute when args.Command.IsMuted is bool isMuted:
                ApplyChannelMute(controls, isMuted);
                break;
            default:
                AppendLog($"Serial command ignored: incomplete command for {args.Command.ChannelName}.");
                break;
        }
    }

    private void SaveSettings()
    {
        settings.Channels = channels;
        settings.RoutingRulesInitialized = true;

        try
        {
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            AppendLog($"Settings save error: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        if (logTextBox.IsDisposed || Disposing || IsDisposed)
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        var shouldAutoScroll = autoScrollLogCheckBox?.Checked ?? true;
        var selectionStart = logTextBox.SelectionStart;
        var selectionLength = logTextBox.SelectionLength;
        logTextBox.AppendText(line);
        if (!shouldAutoScroll)
        {
            logTextBox.SelectionStart = Math.Min(selectionStart, logTextBox.TextLength);
            logTextBox.SelectionLength = Math.Min(selectionLength, logTextBox.TextLength - logTextBox.SelectionStart);
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        if (!InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class ChannelControls(
        MixerChannel channel,
        DualMixChannelStripControl stripControl)
    {
        public MixerChannel Channel { get; } = channel;

        public DualMixChannelStripControl StripControl { get; } = stripControl;

        public bool IsUpdating { get; set; }
    }

    private sealed record AutoRoutingAttempt(DateTime Utc, string Status, string PidSignature = "");

    private sealed record AutoRoutingRequest(
        string ProcessName,
        string AssignedChannel,
        string TargetEndpointId,
        string TargetEndpointFriendlyName,
        IReadOnlyList<int> ProcessIds);

    private sealed record AutoRoutingOutcome(
        AutoRoutingRequest Request,
        IReadOnlyList<AppOutputRoutingResult> Results);

    private sealed class EndpointChoice
    {
        public static readonly EndpointChoice Empty = new(null);

        public EndpointChoice(AudioEndpoint? endpoint)
        {
            Endpoint = endpoint;
        }

        public AudioEndpoint? Endpoint { get; }

        public override string ToString() => Endpoint?.DisplayName ?? "Select endpoint...";
    }
}
