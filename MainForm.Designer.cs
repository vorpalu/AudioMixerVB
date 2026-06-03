#nullable enable

namespace AudioMixerVB;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel rootLayout = null!;
    private Label titleLabel = null!;
    private TabControl mainTabControl = null!;
    private TabPage mixerTabPage = null!;
    private TabPage routingTabPage = null!;
    private TabPage monitorTabPage = null!;
    private TabPage logsTabPage = null!;
    private TabPage settingsTabPage = null!;
    private TableLayoutPanel channelsContainer = null!;
    private FlowLayoutPanel endpointToolbarPanel = null!;
    private Button refreshEndpointsButton = null!;
    private TableLayoutPanel channelsTable = null!;
    private GroupBox serialGroupBox = null!;
    private TableLayoutPanel serialLayout = null!;
    private Label serialPortLabel = null!;
    private ComboBox serialPortComboBox = null!;
    private Button refreshComButton = null!;
    private Label baudRateLabel = null!;
    private TextBox baudRateTextBox = null!;
    private Button connectSerialButton = null!;
    private Label serialStatusCaptionLabel = null!;
    private Label serialStatusValueLabel = null!;
    private GroupBox appRoutingGroupBox = null!;
    private TableLayoutPanel appRoutingLayout = null!;
    private FlowLayoutPanel appRoutingToolbarPanel = null!;
    private Label routingHintLabel = null!;
    private Button refreshAppsButton = null!;
    private Button applyRoutingButton = null!;
    private Button saveRoutingRulesButton = null!;
    private Button clearRoutingButton = null!;
    private Button openWindowsVolumeMixerButton = null!;
    private CheckBox enableExperimentalRoutingCheckBox = null!;
    private CheckBox autoApplyRoutingRulesCheckBox = null!;
    private CheckBox showRawSessionsCheckBox = null!;
    private DataGridView appSessionsGrid = null!;
    private System.Windows.Forms.Timer autoApplyRoutingTimer = null!;
    private System.Windows.Forms.Timer meterUpdateTimer = null!;
    private GroupBox logGroupBox = null!;
    private TextBox logTextBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        titleLabel = new Label();
        mainTabControl = new TabControl();
        mixerTabPage = new TabPage();
        routingTabPage = new TabPage();
        monitorTabPage = new TabPage();
        logsTabPage = new TabPage();
        settingsTabPage = new TabPage();
        channelsContainer = new TableLayoutPanel();
        endpointToolbarPanel = new FlowLayoutPanel();
        refreshEndpointsButton = new Button();
        channelsTable = new TableLayoutPanel();
        serialGroupBox = new GroupBox();
        serialLayout = new TableLayoutPanel();
        serialPortLabel = new Label();
        serialPortComboBox = new ComboBox();
        refreshComButton = new Button();
        baudRateLabel = new Label();
        baudRateTextBox = new TextBox();
        connectSerialButton = new Button();
        serialStatusCaptionLabel = new Label();
        serialStatusValueLabel = new Label();
        appRoutingGroupBox = new GroupBox();
        appRoutingLayout = new TableLayoutPanel();
        appRoutingToolbarPanel = new FlowLayoutPanel();
        routingHintLabel = new Label();
        refreshAppsButton = new Button();
        applyRoutingButton = new Button();
        saveRoutingRulesButton = new Button();
        clearRoutingButton = new Button();
        openWindowsVolumeMixerButton = new Button();
        enableExperimentalRoutingCheckBox = new CheckBox();
        autoApplyRoutingRulesCheckBox = new CheckBox();
        showRawSessionsCheckBox = new CheckBox();
        appSessionsGrid = new DataGridView();
        autoApplyRoutingTimer = new System.Windows.Forms.Timer(components);
        meterUpdateTimer = new System.Windows.Forms.Timer(components);
        logGroupBox = new GroupBox();
        logTextBox = new TextBox();
        rootLayout.SuspendLayout();
        mainTabControl.SuspendLayout();
        mixerTabPage.SuspendLayout();
        channelsContainer.SuspendLayout();
        endpointToolbarPanel.SuspendLayout();
        routingTabPage.SuspendLayout();
        appRoutingGroupBox.SuspendLayout();
        appRoutingLayout.SuspendLayout();
        appRoutingToolbarPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)appSessionsGrid).BeginInit();
        logsTabPage.SuspendLayout();
        logGroupBox.SuspendLayout();
        settingsTabPage.SuspendLayout();
        serialGroupBox.SuspendLayout();
        serialLayout.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(titleLabel, 0, 0);
        rootLayout.Controls.Add(mainTabControl, 0, 1);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(12);
        rootLayout.RowCount = 2;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.Size = new Size(1184, 761);
        rootLayout.TabIndex = 0;
        // 
        // titleLabel
        // 
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
        titleLabel.Location = new Point(15, 12);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(1154, 52);
        titleLabel.TabIndex = 0;
        titleLabel.Text = "AudioMixerVB";
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // mainTabControl
        // 
        mainTabControl.Controls.Add(mixerTabPage);
        mainTabControl.Controls.Add(routingTabPage);
        mainTabControl.Controls.Add(monitorTabPage);
        mainTabControl.Controls.Add(logsTabPage);
        mainTabControl.Controls.Add(settingsTabPage);
        mainTabControl.Dock = DockStyle.Fill;
        mainTabControl.Location = new Point(15, 67);
        mainTabControl.Name = "mainTabControl";
        mainTabControl.SelectedIndex = 0;
        mainTabControl.Size = new Size(1154, 679);
        mainTabControl.TabIndex = 1;
        // 
        // mixerTabPage
        // 
        mixerTabPage.Controls.Add(channelsContainer);
        mixerTabPage.Location = new Point(4, 29);
        mixerTabPage.Name = "mixerTabPage";
        mixerTabPage.Padding = new Padding(8);
        mixerTabPage.Size = new Size(1146, 646);
        mixerTabPage.TabIndex = 0;
        mixerTabPage.Text = "Mixer";
        mixerTabPage.UseVisualStyleBackColor = true;
        // 
        // routingTabPage
        // 
        routingTabPage.Controls.Add(appRoutingGroupBox);
        routingTabPage.Location = new Point(4, 29);
        routingTabPage.Name = "routingTabPage";
        routingTabPage.Padding = new Padding(8);
        routingTabPage.Size = new Size(1146, 646);
        routingTabPage.TabIndex = 1;
        routingTabPage.Text = "Routing";
        routingTabPage.UseVisualStyleBackColor = true;
        // 
        // monitorTabPage
        // 
        monitorTabPage.Location = new Point(4, 29);
        monitorTabPage.Name = "monitorTabPage";
        monitorTabPage.Padding = new Padding(8);
        monitorTabPage.Size = new Size(1146, 646);
        monitorTabPage.TabIndex = 2;
        monitorTabPage.Text = "Monitor";
        monitorTabPage.UseVisualStyleBackColor = true;
        // 
        // logsTabPage
        // 
        logsTabPage.Controls.Add(logGroupBox);
        logsTabPage.Location = new Point(4, 29);
        logsTabPage.Name = "logsTabPage";
        logsTabPage.Padding = new Padding(8);
        logsTabPage.Size = new Size(1146, 646);
        logsTabPage.TabIndex = 3;
        logsTabPage.Text = "Logs";
        logsTabPage.UseVisualStyleBackColor = true;
        // 
        // settingsTabPage
        // 
        settingsTabPage.Controls.Add(serialGroupBox);
        settingsTabPage.Location = new Point(4, 29);
        settingsTabPage.Name = "settingsTabPage";
        settingsTabPage.Padding = new Padding(8);
        settingsTabPage.Size = new Size(1146, 646);
        settingsTabPage.TabIndex = 4;
        settingsTabPage.Text = "Settings";
        settingsTabPage.UseVisualStyleBackColor = true;
        // 
        // channelsContainer
        // 
        channelsContainer.ColumnCount = 1;
        channelsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        channelsContainer.Controls.Add(endpointToolbarPanel, 0, 0);
        channelsContainer.Controls.Add(channelsTable, 0, 1);
        channelsContainer.Dock = DockStyle.Fill;
        channelsContainer.Location = new Point(8, 8);
        channelsContainer.Name = "channelsContainer";
        channelsContainer.RowCount = 2;
        channelsContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        channelsContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        channelsContainer.Size = new Size(1130, 630);
        channelsContainer.TabIndex = 0;
        // 
        // endpointToolbarPanel
        // 
        endpointToolbarPanel.Controls.Add(refreshEndpointsButton);
        endpointToolbarPanel.Dock = DockStyle.Fill;
        endpointToolbarPanel.FlowDirection = FlowDirection.RightToLeft;
        endpointToolbarPanel.Location = new Point(3, 3);
        endpointToolbarPanel.Name = "endpointToolbarPanel";
        endpointToolbarPanel.Size = new Size(1124, 36);
        endpointToolbarPanel.TabIndex = 0;
        endpointToolbarPanel.WrapContents = false;
        // 
        // refreshEndpointsButton
        // 
        refreshEndpointsButton.AutoSize = true;
        refreshEndpointsButton.Location = new Point(969, 3);
        refreshEndpointsButton.Name = "refreshEndpointsButton";
        refreshEndpointsButton.Size = new Size(152, 30);
        refreshEndpointsButton.TabIndex = 0;
        refreshEndpointsButton.Text = "Refresh endpoints";
        refreshEndpointsButton.UseVisualStyleBackColor = true;
        // 
        // channelsTable
        // 
        channelsTable.ColumnCount = 2;
        channelsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        channelsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        channelsTable.Dock = DockStyle.Fill;
        channelsTable.Location = new Point(3, 45);
        channelsTable.Name = "channelsTable";
        channelsTable.RowCount = 2;
        channelsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        channelsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        channelsTable.Size = new Size(1124, 582);
        channelsTable.TabIndex = 1;
        // 
        // serialGroupBox
        // 
        serialGroupBox.Controls.Add(serialLayout);
        serialGroupBox.Dock = DockStyle.Top;
        serialGroupBox.Location = new Point(8, 8);
        serialGroupBox.Name = "serialGroupBox";
        serialGroupBox.Padding = new Padding(10);
        serialGroupBox.Size = new Size(1130, 150);
        serialGroupBox.TabIndex = 0;
        serialGroupBox.TabStop = false;
        serialGroupBox.Text = "Serial";
        // 
        // serialLayout
        // 
        serialLayout.ColumnCount = 4;
        serialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
        serialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        serialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102F));
        serialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106F));
        serialLayout.Controls.Add(serialPortLabel, 0, 0);
        serialLayout.Controls.Add(serialPortComboBox, 1, 0);
        serialLayout.Controls.Add(refreshComButton, 3, 0);
        serialLayout.Controls.Add(baudRateLabel, 0, 1);
        serialLayout.Controls.Add(baudRateTextBox, 1, 1);
        serialLayout.Controls.Add(connectSerialButton, 3, 1);
        serialLayout.Controls.Add(serialStatusCaptionLabel, 0, 2);
        serialLayout.Controls.Add(serialStatusValueLabel, 1, 2);
        serialLayout.Dock = DockStyle.Fill;
        serialLayout.Location = new Point(10, 30);
        serialLayout.Name = "serialLayout";
        serialLayout.RowCount = 3;
        serialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        serialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        serialLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        serialLayout.Size = new Size(1110, 110);
        serialLayout.TabIndex = 0;
        serialLayout.SetColumnSpan(serialPortComboBox, 2);
        serialLayout.SetColumnSpan(baudRateTextBox, 2);
        serialLayout.SetColumnSpan(serialStatusValueLabel, 3);
        // 
        // serialPortLabel
        // 
        serialPortLabel.Dock = DockStyle.Fill;
        serialPortLabel.Location = new Point(3, 0);
        serialPortLabel.Name = "serialPortLabel";
        serialPortLabel.Size = new Size(68, 38);
        serialPortLabel.TabIndex = 0;
        serialPortLabel.Text = "COM";
        serialPortLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // serialPortComboBox
        // 
        serialPortComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        serialPortComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        serialPortComboBox.FormattingEnabled = true;
        serialPortComboBox.Location = new Point(77, 5);
        serialPortComboBox.Name = "serialPortComboBox";
        serialPortComboBox.Size = new Size(921, 28);
        serialPortComboBox.TabIndex = 1;
        // 
        // refreshComButton
        // 
        refreshComButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        refreshComButton.Location = new Point(1007, 4);
        refreshComButton.Name = "refreshComButton";
        refreshComButton.Size = new Size(100, 30);
        refreshComButton.TabIndex = 2;
        refreshComButton.Text = "Refresh";
        refreshComButton.UseVisualStyleBackColor = true;
        // 
        // baudRateLabel
        // 
        baudRateLabel.Dock = DockStyle.Fill;
        baudRateLabel.Location = new Point(3, 38);
        baudRateLabel.Name = "baudRateLabel";
        baudRateLabel.Size = new Size(68, 38);
        baudRateLabel.TabIndex = 3;
        baudRateLabel.Text = "Baud";
        baudRateLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // baudRateTextBox
        // 
        baudRateTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        baudRateTextBox.Location = new Point(77, 43);
        baudRateTextBox.Name = "baudRateTextBox";
        baudRateTextBox.Size = new Size(921, 27);
        baudRateTextBox.TabIndex = 4;
        baudRateTextBox.Text = "115200";
        // 
        // connectSerialButton
        // 
        connectSerialButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        connectSerialButton.Location = new Point(1007, 42);
        connectSerialButton.Name = "connectSerialButton";
        connectSerialButton.Size = new Size(100, 30);
        connectSerialButton.TabIndex = 5;
        connectSerialButton.Text = "Connect";
        connectSerialButton.UseVisualStyleBackColor = true;
        // 
        // serialStatusCaptionLabel
        // 
        serialStatusCaptionLabel.Dock = DockStyle.Fill;
        serialStatusCaptionLabel.Location = new Point(3, 76);
        serialStatusCaptionLabel.Name = "serialStatusCaptionLabel";
        serialStatusCaptionLabel.Size = new Size(68, 34);
        serialStatusCaptionLabel.TabIndex = 6;
        serialStatusCaptionLabel.Text = "Status";
        serialStatusCaptionLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // serialStatusValueLabel
        // 
        serialStatusValueLabel.Dock = DockStyle.Fill;
        serialStatusValueLabel.Location = new Point(77, 76);
        serialStatusValueLabel.Name = "serialStatusValueLabel";
        serialStatusValueLabel.Size = new Size(1030, 34);
        serialStatusValueLabel.TabIndex = 7;
        serialStatusValueLabel.Text = "Disconnected";
        serialStatusValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // appRoutingGroupBox
        // 
        appRoutingGroupBox.Controls.Add(appRoutingLayout);
        appRoutingGroupBox.Dock = DockStyle.Fill;
        appRoutingGroupBox.Location = new Point(8, 8);
        appRoutingGroupBox.Name = "appRoutingGroupBox";
        appRoutingGroupBox.Padding = new Padding(10);
        appRoutingGroupBox.Size = new Size(1130, 630);
        appRoutingGroupBox.TabIndex = 0;
        appRoutingGroupBox.TabStop = false;
        appRoutingGroupBox.Text = "Application Routing";
        // 
        // appRoutingLayout
        // 
        appRoutingLayout.ColumnCount = 1;
        appRoutingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        appRoutingLayout.Controls.Add(appRoutingToolbarPanel, 0, 0);
        appRoutingLayout.Controls.Add(routingHintLabel, 0, 1);
        appRoutingLayout.Controls.Add(appSessionsGrid, 0, 2);
        appRoutingLayout.Dock = DockStyle.Fill;
        appRoutingLayout.Location = new Point(10, 30);
        appRoutingLayout.Name = "appRoutingLayout";
        appRoutingLayout.RowCount = 3;
        appRoutingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        appRoutingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        appRoutingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        appRoutingLayout.Size = new Size(1110, 590);
        appRoutingLayout.TabIndex = 0;
        // 
        // appRoutingToolbarPanel
        // 
        appRoutingToolbarPanel.Controls.Add(refreshAppsButton);
        appRoutingToolbarPanel.Controls.Add(applyRoutingButton);
        appRoutingToolbarPanel.Controls.Add(saveRoutingRulesButton);
        appRoutingToolbarPanel.Controls.Add(clearRoutingButton);
        appRoutingToolbarPanel.Controls.Add(openWindowsVolumeMixerButton);
        appRoutingToolbarPanel.Controls.Add(enableExperimentalRoutingCheckBox);
        appRoutingToolbarPanel.Controls.Add(autoApplyRoutingRulesCheckBox);
        appRoutingToolbarPanel.Controls.Add(showRawSessionsCheckBox);
        appRoutingToolbarPanel.Dock = DockStyle.Fill;
        appRoutingToolbarPanel.Location = new Point(3, 3);
        appRoutingToolbarPanel.Name = "appRoutingToolbarPanel";
        appRoutingToolbarPanel.Size = new Size(1104, 70);
        appRoutingToolbarPanel.TabIndex = 0;
        appRoutingToolbarPanel.WrapContents = true;
        // 
        // refreshAppsButton
        // 
        refreshAppsButton.AutoSize = true;
        refreshAppsButton.Location = new Point(3, 3);
        refreshAppsButton.Name = "refreshAppsButton";
        refreshAppsButton.Size = new Size(106, 30);
        refreshAppsButton.TabIndex = 0;
        refreshAppsButton.Text = "Refresh Apps";
        refreshAppsButton.UseVisualStyleBackColor = true;
        // 
        // applyRoutingButton
        // 
        applyRoutingButton.AutoSize = true;
        applyRoutingButton.Location = new Point(115, 3);
        applyRoutingButton.Name = "applyRoutingButton";
        applyRoutingButton.Size = new Size(117, 30);
        applyRoutingButton.TabIndex = 1;
        applyRoutingButton.Text = "Apply Routing";
        applyRoutingButton.UseVisualStyleBackColor = true;
        // 
        // saveRoutingRulesButton
        // 
        saveRoutingRulesButton.AutoSize = true;
        saveRoutingRulesButton.Location = new Point(238, 3);
        saveRoutingRulesButton.Name = "saveRoutingRulesButton";
        saveRoutingRulesButton.Size = new Size(132, 30);
        saveRoutingRulesButton.TabIndex = 2;
        saveRoutingRulesButton.Text = "Save Routing";
        saveRoutingRulesButton.UseVisualStyleBackColor = true;
        // 
        // clearRoutingButton
        // 
        clearRoutingButton.AutoSize = true;
        clearRoutingButton.Location = new Point(376, 3);
        clearRoutingButton.Name = "clearRoutingButton";
        clearRoutingButton.Size = new Size(107, 30);
        clearRoutingButton.TabIndex = 3;
        clearRoutingButton.Text = "Clear";
        clearRoutingButton.UseVisualStyleBackColor = true;
        // 
        // openWindowsVolumeMixerButton
        // 
        openWindowsVolumeMixerButton.AutoSize = true;
        openWindowsVolumeMixerButton.Location = new Point(489, 3);
        openWindowsVolumeMixerButton.Name = "openWindowsVolumeMixerButton";
        openWindowsVolumeMixerButton.Size = new Size(191, 30);
        openWindowsVolumeMixerButton.TabIndex = 4;
        openWindowsVolumeMixerButton.Text = "Open Volume Mixer";
        openWindowsVolumeMixerButton.UseVisualStyleBackColor = true;
        // 
        // enableExperimentalRoutingCheckBox
        // 
        enableExperimentalRoutingCheckBox.AutoSize = true;
        enableExperimentalRoutingCheckBox.Location = new Point(686, 3);
        enableExperimentalRoutingCheckBox.Name = "enableExperimentalRoutingCheckBox";
        enableExperimentalRoutingCheckBox.Size = new Size(132, 24);
        enableExperimentalRoutingCheckBox.TabIndex = 5;
        enableExperimentalRoutingCheckBox.Text = "Experimental";
        enableExperimentalRoutingCheckBox.UseVisualStyleBackColor = true;
        // 
        // autoApplyRoutingRulesCheckBox
        // 
        autoApplyRoutingRulesCheckBox.AutoSize = true;
        autoApplyRoutingRulesCheckBox.Location = new Point(824, 3);
        autoApplyRoutingRulesCheckBox.Name = "autoApplyRoutingRulesCheckBox";
        autoApplyRoutingRulesCheckBox.Size = new Size(102, 24);
        autoApplyRoutingRulesCheckBox.TabIndex = 6;
        autoApplyRoutingRulesCheckBox.Text = "Auto apply";
        autoApplyRoutingRulesCheckBox.UseVisualStyleBackColor = true;
        // 
        // showRawSessionsCheckBox
        // 
        showRawSessionsCheckBox.AutoSize = true;
        showRawSessionsCheckBox.Location = new Point(932, 3);
        showRawSessionsCheckBox.Name = "showRawSessionsCheckBox";
        showRawSessionsCheckBox.Size = new Size(154, 24);
        showRawSessionsCheckBox.TabIndex = 7;
        showRawSessionsCheckBox.Text = "Show raw sessions";
        showRawSessionsCheckBox.UseVisualStyleBackColor = true;
        // 
        // routingHintLabel
        // 
        routingHintLabel.Dock = DockStyle.Fill;
        routingHintLabel.ForeColor = Color.DimGray;
        routingHintLabel.Location = new Point(3, 76);
        routingHintLabel.Name = "routingHintLabel";
        routingHintLabel.Size = new Size(1104, 42);
        routingHintLabel.TabIndex = 7;
        routingHintLabel.Text = "Experimental routing changes Windows per-app output preference. Some apps need restart before moving.";
        routingHintLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // appSessionsGrid
        // 
        appSessionsGrid.AllowUserToAddRows = false;
        appSessionsGrid.AllowUserToDeleteRows = false;
        appSessionsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        appSessionsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        appSessionsGrid.Dock = DockStyle.Fill;
        appSessionsGrid.Location = new Point(3, 121);
        appSessionsGrid.MultiSelect = false;
        appSessionsGrid.Name = "appSessionsGrid";
        appSessionsGrid.RowHeadersVisible = false;
        appSessionsGrid.RowHeadersWidth = 51;
        appSessionsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        appSessionsGrid.Size = new Size(1104, 466);
        appSessionsGrid.TabIndex = 1;
        // 
        // autoApplyRoutingTimer
        // 
        autoApplyRoutingTimer.Interval = 3000;
        // 
        // meterUpdateTimer
        // 
        meterUpdateTimer.Interval = 40;
        // 
        // logGroupBox
        // 
        logGroupBox.Controls.Add(logTextBox);
        logGroupBox.Dock = DockStyle.Fill;
        logGroupBox.Location = new Point(8, 8);
        logGroupBox.Name = "logGroupBox";
        logGroupBox.Padding = new Padding(10);
        logGroupBox.Size = new Size(1130, 630);
        logGroupBox.TabIndex = 0;
        logGroupBox.TabStop = false;
        logGroupBox.Text = "Log";
        // 
        // logTextBox
        // 
        logTextBox.BackColor = Color.White;
        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Font = new Font("Consolas", 9F);
        logTextBox.Location = new Point(10, 30);
        logTextBox.Multiline = true;
        logTextBox.Name = "logTextBox";
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.Size = new Size(1110, 590);
        logTextBox.TabIndex = 0;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(8F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1184, 761);
        Controls.Add(rootLayout);
        MinimumSize = new Size(1040, 700);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "AudioMixerVB";
        rootLayout.ResumeLayout(false);
        mainTabControl.ResumeLayout(false);
        mixerTabPage.ResumeLayout(false);
        channelsContainer.ResumeLayout(false);
        endpointToolbarPanel.ResumeLayout(false);
        endpointToolbarPanel.PerformLayout();
        routingTabPage.ResumeLayout(false);
        appRoutingGroupBox.ResumeLayout(false);
        appRoutingLayout.ResumeLayout(false);
        appRoutingToolbarPanel.ResumeLayout(false);
        appRoutingToolbarPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)appSessionsGrid).EndInit();
        logsTabPage.ResumeLayout(false);
        logGroupBox.ResumeLayout(false);
        logGroupBox.PerformLayout();
        settingsTabPage.ResumeLayout(false);
        serialGroupBox.ResumeLayout(false);
        serialLayout.ResumeLayout(false);
        serialLayout.PerformLayout();
        ResumeLayout(false);
    }
}
