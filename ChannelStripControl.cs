using System.ComponentModel;

namespace AudioMixerVB;

public sealed class ChannelStripControl : UserControl
{
    private readonly Panel accentPanel = new();
    private readonly TableLayoutPanel layout = new();
    private readonly Label nameLabel = new();
    private readonly ComboBox endpointComboBox = new();
    private readonly TrackBar volumeTrackBar = new();
    private readonly Label volumeLabel = new();
    private readonly Button muteButton = new();
    private readonly Label statusLabel = new();
    private readonly VuMeterControl vuMeterControl = new();

    public ChannelStripControl()
    {
        Dock = DockStyle.Fill;
        Margin = new Padding(8);
        MinimumSize = new Size(128, 360);
        BackColor = Color.FromArgb(31, 34, 40);
        Padding = new Padding(1);

        accentPanel.Dock = DockStyle.Top;
        accentPanel.Height = 4;
        accentPanel.BackColor = Color.DeepSkyBlue;

        layout.ColumnCount = 2;
        layout.Dock = DockStyle.Fill;
        layout.RowCount = 6;
        layout.BackColor = BackColor;
        layout.Padding = new Padding(8);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        nameLabel.Dock = DockStyle.Fill;
        nameLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        nameLabel.ForeColor = Color.WhiteSmoke;
        nameLabel.Text = "Channel";
        nameLabel.TextAlign = ContentAlignment.MiddleCenter;

        endpointComboBox.Dock = DockStyle.Fill;
        endpointComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        endpointComboBox.FormattingEnabled = true;

        volumeTrackBar.Dock = DockStyle.Fill;
        volumeTrackBar.Minimum = 0;
        volumeTrackBar.Maximum = 100;
        volumeTrackBar.TickFrequency = 10;
        volumeTrackBar.LargeChange = 5;
        volumeTrackBar.SmallChange = 1;
        volumeTrackBar.Orientation = Orientation.Vertical;
        volumeTrackBar.TickStyle = TickStyle.Both;
        volumeTrackBar.Value = MixerChannel.DefaultVolumePercent;
        volumeTrackBar.Enabled = false;

        volumeLabel.Dock = DockStyle.Fill;
        volumeLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        volumeLabel.ForeColor = Color.WhiteSmoke;
        volumeLabel.Text = $"{MixerChannel.DefaultVolumePercent}%";
        volumeLabel.TextAlign = ContentAlignment.MiddleCenter;

        muteButton.Dock = DockStyle.Fill;
        muteButton.Enabled = false;
        muteButton.Text = "Mute";
        muteButton.UseVisualStyleBackColor = false;
        muteButton.BackColor = Color.FromArgb(48, 52, 60);
        muteButton.ForeColor = Color.WhiteSmoke;
        muteButton.FlatStyle = FlatStyle.Flat;

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.FromArgb(190, 196, 205);
        statusLabel.Text = "Not found";
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;

        vuMeterControl.Dock = DockStyle.Fill;
        vuMeterControl.Value = 0;

        layout.Controls.Add(nameLabel, 0, 0);
        layout.SetColumnSpan(nameLabel, 2);
        layout.Controls.Add(vuMeterControl, 0, 1);
        layout.SetRowSpan(vuMeterControl, 2);
        layout.Controls.Add(volumeTrackBar, 1, 1);
        layout.Controls.Add(volumeLabel, 1, 2);
        layout.Controls.Add(muteButton, 0, 3);
        layout.SetColumnSpan(muteButton, 2);
        layout.Controls.Add(statusLabel, 0, 4);
        layout.SetColumnSpan(statusLabel, 2);
        layout.Controls.Add(endpointComboBox, 0, 5);
        layout.SetColumnSpan(endpointComboBox, 2);

        Controls.Add(layout);
        Controls.Add(accentPanel);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ChannelName
    {
        get => nameLabel.Text;
        set => nameLabel.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor
    {
        get => accentPanel.BackColor;
        set => accentPanel.BackColor = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool EndpointComboBoxVisible
    {
        get => endpointComboBox.Visible;
        set => endpointComboBox.Visible = value;
    }

    public ComboBox EndpointComboBox => endpointComboBox;

    public TrackBar VolumeTrackBar => volumeTrackBar;

    public Button MuteButton => muteButton;

    public Label StatusLabel => statusLabel;

    public VuMeterControl VuMeter => vuMeterControl;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int VolumePercent
    {
        get => volumeTrackBar.Value;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            volumeTrackBar.Value = normalized;
            volumeLabel.Text = $"{normalized}%";
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMuted
    {
        get => vuMeterControl.IsMuted;
        set
        {
            muteButton.Text = value ? "Unmute" : "Mute";
            vuMeterControl.IsMuted = value;
        }
    }

    public void SetEndpointControlsEnabled(bool enabled)
    {
        volumeTrackBar.Enabled = enabled;
        muteButton.Enabled = enabled;
    }

    public void SetStatus(string status, Color color)
    {
        statusLabel.Text = status;
        statusLabel.ForeColor = color;
    }
}
