using System.ComponentModel;

namespace AudioMixerVB;

public sealed class DualMixChannelStripControl : UserControl
{
    private static readonly Color SurfaceColor = Color.FromArgb(32, 36, 43);
    private static readonly Color InnerColor = Color.FromArgb(24, 27, 33);
    private static readonly Color BorderColor = Color.FromArgb(52, 58, 68);
    private static readonly Color PrimaryTextColor = Color.FromArgb(242, 244, 248);
    private static readonly Color SecondaryTextColor = Color.FromArgb(170, 176, 188);

    private readonly Panel accentPanel = new();
    private readonly TableLayoutPanel layout = new();
    private readonly Label iconLabel = new();
    private readonly Label nameLabel = new();
    private readonly ComboBox endpointComboBox = new();
    private readonly TableLayoutPanel mixesLayout = new();
    private readonly Label appSummaryLabel = new();
    private readonly VuMeterControl monitorVuMeter = new();
    private readonly VuMeterControl streamVuMeter = new();
    private readonly TrackBar monitorTrackBar = new();
    private readonly TrackBar streamTrackBar = new();
    private readonly Label monitorPercentLabel = new();
    private readonly Label streamPercentLabel = new();
    private readonly Button monitorMuteButton = new();
    private readonly Button streamMuteButton = new();

    private Color accentColor = Color.DeepSkyBlue;

    public DualMixChannelStripControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        Dock = DockStyle.Fill;
        Margin = new Padding(6);
        MinimumSize = new Size(168, 452);
        BackColor = SurfaceColor;
        Padding = new Padding(1);

        accentPanel.Dock = DockStyle.Top;
        accentPanel.Height = 5;
        accentPanel.BackColor = accentColor;

        layout.ColumnCount = 1;
        layout.Dock = DockStyle.Fill;
        layout.RowCount = 5;
        layout.BackColor = SurfaceColor;
        layout.Padding = new Padding(8, 9, 8, 8);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));

        iconLabel.Dock = DockStyle.Fill;
        iconLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        iconLabel.ForeColor = SecondaryTextColor;
        iconLabel.Text = "CH";
        iconLabel.TextAlign = ContentAlignment.MiddleCenter;

        nameLabel.Dock = DockStyle.Fill;
        nameLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        nameLabel.ForeColor = PrimaryTextColor;
        nameLabel.Text = "Channel";
        nameLabel.TextAlign = ContentAlignment.MiddleCenter;

        endpointComboBox.Dock = DockStyle.Fill;
        endpointComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        endpointComboBox.FlatStyle = FlatStyle.Flat;
        endpointComboBox.BackColor = InnerColor;
        endpointComboBox.ForeColor = PrimaryTextColor;
        endpointComboBox.Font = new Font("Segoe UI", 8F);
        endpointComboBox.Margin = new Padding(0, 2, 0, 3);
        endpointComboBox.DropDownWidth = 320;

        mixesLayout.ColumnCount = 2;
        mixesLayout.Dock = DockStyle.Fill;
        mixesLayout.RowCount = 1;
        mixesLayout.BackColor = SurfaceColor;
        mixesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        mixesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        mixesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mixesLayout.Controls.Add(CreateMixColumn("MON", monitorVuMeter, monitorTrackBar, monitorPercentLabel, monitorMuteButton), 0, 0);
        mixesLayout.Controls.Add(CreateMixColumn("STR", streamVuMeter, streamTrackBar, streamPercentLabel, streamMuteButton), 1, 0);

        appSummaryLabel.Dock = DockStyle.Fill;
        appSummaryLabel.BackColor = InnerColor;
        appSummaryLabel.ForeColor = SecondaryTextColor;
        appSummaryLabel.Font = new Font("Segoe UI", 8.5F);
        appSummaryLabel.Text = "No apps";
        appSummaryLabel.TextAlign = ContentAlignment.MiddleCenter;
        appSummaryLabel.Padding = new Padding(5);
        appSummaryLabel.AutoEllipsis = true;

        layout.Controls.Add(iconLabel, 0, 0);
        layout.Controls.Add(nameLabel, 0, 1);
        layout.Controls.Add(endpointComboBox, 0, 2);
        layout.Controls.Add(mixesLayout, 0, 3);
        layout.Controls.Add(appSummaryLabel, 0, 4);

        Controls.Add(layout);
        Controls.Add(accentPanel);

        MonitorPercent = 50;
        StreamPercent = 100;
        MonitorMuted = false;
        StreamMuted = false;
    }

    public TrackBar MonitorTrackBar => monitorTrackBar;

    public TrackBar StreamTrackBar => streamTrackBar;

    public Button MonitorMuteButton => monitorMuteButton;

    public Button StreamMuteButton => streamMuteButton;

    public VuMeterControl MonitorVuMeter => monitorVuMeter;

    public VuMeterControl StreamVuMeter => streamVuMeter;

    public ComboBox EndpointComboBox => endpointComboBox;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ChannelName
    {
        get => nameLabel.Text;
        set => nameLabel.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string IconText
    {
        get => iconLabel.Text;
        set => iconLabel.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor
    {
        get => accentColor;
        set
        {
            accentColor = value;
            accentPanel.BackColor = value;
            monitorMuteButton.FlatAppearance.BorderColor = value;
            streamMuteButton.FlatAppearance.BorderColor = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MonitorPercent
    {
        get => monitorTrackBar.Value;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            monitorTrackBar.Value = normalized;
            monitorPercentLabel.Text = $"{normalized}%";
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int StreamPercent
    {
        get => streamTrackBar.Value;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            streamTrackBar.Value = normalized;
            streamPercentLabel.Text = $"{normalized}%";
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool MonitorMuted
    {
        get => monitorVuMeter.IsMuted;
        set
        {
            monitorMuteButton.Text = value ? "Unmute" : "Mute";
            monitorVuMeter.IsMuted = value;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool StreamMuted
    {
        get => streamVuMeter.IsMuted;
        set
        {
            streamMuteButton.Text = value ? "Unmute" : "Mute";
            streamVuMeter.IsMuted = value;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string AppSummary
    {
        get => appSummaryLabel.Text;
        set => appSummaryLabel.Text = string.IsNullOrWhiteSpace(value) ? "No apps" : value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool EndpointComboBoxVisible
    {
        get => endpointComboBox.Visible;
        set => endpointComboBox.Visible = value;
    }

    public void SetMixControlsEnabled(bool enabled)
    {
        monitorTrackBar.Enabled = enabled;
        streamTrackBar.Enabled = enabled;
        monitorMuteButton.Enabled = enabled;
        streamMuteButton.Enabled = enabled;
    }

    public void SetEndpointSelectorEnabled(bool enabled)
    {
        endpointComboBox.Enabled = enabled;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        using var borderPen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    private static TableLayoutPanel CreateMixColumn(
        string labelText,
        VuMeterControl vuMeter,
        TrackBar trackBar,
        Label percentLabel,
        Button muteButton)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 4,
            BackColor = InnerColor,
            Margin = new Padding(3),
            Padding = new Padding(5)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

        var mixLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = SecondaryTextColor,
            Text = labelText,
            TextAlign = ContentAlignment.MiddleCenter
        };

        vuMeter.Dock = DockStyle.Fill;
        vuMeter.Margin = new Padding(0, 2, 4, 2);
        vuMeter.Value = 0;

        trackBar.Dock = DockStyle.Fill;
        trackBar.Minimum = 0;
        trackBar.Maximum = 100;
        trackBar.TickFrequency = 10;
        trackBar.LargeChange = 5;
        trackBar.SmallChange = 1;
        trackBar.Orientation = Orientation.Vertical;
        trackBar.TickStyle = TickStyle.None;
        trackBar.Value = 50;

        percentLabel.Dock = DockStyle.Fill;
        percentLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        percentLabel.ForeColor = PrimaryTextColor;
        percentLabel.Text = "50%";
        percentLabel.TextAlign = ContentAlignment.MiddleCenter;

        muteButton.Dock = DockStyle.Fill;
        muteButton.Text = "Mute";
        muteButton.UseVisualStyleBackColor = false;
        muteButton.BackColor = Color.FromArgb(39, 43, 51);
        muteButton.ForeColor = PrimaryTextColor;
        muteButton.FlatStyle = FlatStyle.Flat;
        muteButton.FlatAppearance.BorderSize = 1;
        muteButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        muteButton.Margin = new Padding(0, 2, 0, 0);

        panel.Controls.Add(mixLabel, 0, 0);
        panel.SetColumnSpan(mixLabel, 2);
        panel.Controls.Add(vuMeter, 0, 1);
        panel.Controls.Add(trackBar, 1, 1);
        panel.Controls.Add(percentLabel, 0, 2);
        panel.SetColumnSpan(percentLabel, 2);
        panel.Controls.Add(muteButton, 0, 3);
        panel.SetColumnSpan(muteButton, 2);

        return panel;
    }
}
