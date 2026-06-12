namespace AudioMixerVB;

public enum AppChipStatus
{
    Neutral,
    Routed,
    Pending,
    Error
}

public sealed record AppChipDragData(string ProcessName);

public sealed class AppChipControl : UserControl
{
    private static readonly Color ChipColor = Color.FromArgb(24, 27, 33);
    private static readonly Color HoverColor = Color.FromArgb(37, 42, 51);
    private static readonly Color BorderColor = Color.FromArgb(52, 58, 68);
    private static readonly Color TextColor = Color.FromArgb(242, 244, 248);

    private readonly Panel statusPanel = new();
    private readonly Label nameLabel = new();
    private Point? dragStartPoint;

    public AppChipControl(string processName, AppChipStatus status)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        ProcessName = processName;
        Status = status;
        Height = 22;
        Width = CalculateWidth(processName);
        MinimumSize = new Size(82, 22);
        MaximumSize = new Size(156, 22);
        Margin = new Padding(2, 1, 2, 1);
        BackColor = ChipColor;
        Cursor = Cursors.SizeAll;
        AccessibleName = $"App chip {processName}";

        statusPanel.Dock = DockStyle.Left;
        statusPanel.Width = 4;
        statusPanel.Margin = Padding.Empty;
        statusPanel.BackColor = GetStatusColor(status);
        statusPanel.Cursor = Cursors.SizeAll;

        nameLabel.Dock = DockStyle.Fill;
        nameLabel.AutoEllipsis = true;
        nameLabel.Text = processName;
        nameLabel.Font = new Font("Segoe UI", 7.8F, FontStyle.Bold);
        nameLabel.ForeColor = TextColor;
        nameLabel.TextAlign = ContentAlignment.MiddleLeft;
        nameLabel.Padding = new Padding(6, 0, 4, 1);
        nameLabel.Cursor = Cursors.SizeAll;

        Controls.Add(nameLabel);
        Controls.Add(statusPanel);

        WireMouseDrag(this);
        WireMouseDrag(statusPanel);
        WireMouseDrag(nameLabel);
    }

    public string ProcessName { get; }

    public AppChipStatus Status { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var borderPen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        SetChipBackColor(HoverColor);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetChipBackColor(ChipColor);
    }

    private void WireMouseDrag(Control control)
    {
        control.MouseDown += HandleMouseDown;
        control.MouseMove += HandleMouseMove;
        control.MouseUp += (_, _) => dragStartPoint = null;
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control control)
        {
            return;
        }

        dragStartPoint = PointToClient(control.PointToScreen(e.Location));
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragStartPoint is not Point startPoint ||
            e.Button != MouseButtons.Left ||
            sender is not Control control)
        {
            return;
        }

        var currentPoint = PointToClient(control.PointToScreen(e.Location));
        var dragSize = SystemInformation.DragSize;
        var dragBounds = new Rectangle(
            startPoint.X - dragSize.Width / 2,
            startPoint.Y - dragSize.Height / 2,
            dragSize.Width,
            dragSize.Height);

        if (dragBounds.Contains(currentPoint))
        {
            return;
        }

        dragStartPoint = null;
        var data = new DataObject();
        data.SetData(typeof(AppChipDragData), new AppChipDragData(ProcessName));
        data.SetText(ProcessName);
        DoDragDrop(data, DragDropEffects.Move);
    }

    private void SetChipBackColor(Color color)
    {
        BackColor = color;
        nameLabel.BackColor = color;
    }

    private static int CalculateWidth(string processName)
    {
        var textWidth = TextRenderer.MeasureText(processName, new Font("Segoe UI", 7.8F, FontStyle.Bold)).Width;
        return Math.Clamp(textWidth + 30, 82, 156);
    }

    private static Color GetStatusColor(AppChipStatus status)
    {
        return status switch
        {
            AppChipStatus.Routed => Color.FromArgb(87, 214, 141),
            AppChipStatus.Pending => Color.FromArgb(230, 184, 92),
            AppChipStatus.Error => Color.FromArgb(235, 86, 96),
            _ => Color.FromArgb(90, 96, 108)
        };
    }
}
