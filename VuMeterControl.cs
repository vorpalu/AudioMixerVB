using System.ComponentModel;

namespace AudioMixerVB;

public sealed class VuMeterControl : Control
{
    private int value;
    private bool isMuted;

    public VuMeterControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = Color.FromArgb(26, 28, 32);
        MinimumSize = new Size(18, 72);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (this.value == normalized)
            {
                return;
            }

            this.value = normalized;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LevelPercent
    {
        get => Value;
        set => Value = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMuted
    {
        get => isMuted;
        set
        {
            if (isMuted == value)
            {
                return;
            }

            isMuted = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var meterBounds = Rectangle.Inflate(bounds, -2, -2);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(19, 21, 25));
        e.Graphics.FillRectangle(backgroundBrush, meterBounds);

        var fillPercent = isMuted ? 0 : value;
        if (fillPercent > 0)
        {
            var fillHeight = Math.Max(2, meterBounds.Height * fillPercent / 100);
            var fillBounds = new Rectangle(
                meterBounds.Left,
                meterBounds.Bottom - fillHeight,
                meterBounds.Width,
                fillHeight);

            using var fillBrush = new SolidBrush(GetFillColor(fillPercent));
            e.Graphics.FillRectangle(fillBrush, fillBounds);
        }

        using var borderPen = new Pen(isMuted ? Color.DarkGray : Color.Gray);
        e.Graphics.DrawRectangle(borderPen, meterBounds);
    }

    private static Color GetFillColor(int percent)
    {
        return percent switch
        {
            >= 85 => Color.Firebrick,
            >= 65 => Color.DarkOrange,
            _ => Color.ForestGreen
        };
    }
}
