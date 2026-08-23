using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher;

internal sealed class StartupSplashForm : Form
{
    private readonly System.Windows.Forms.Timer _longStartTimer;
    private readonly Image _applicationImage;
    private readonly Font _titleFont;
    private readonly Label _hintLabel;

    public StartupSplashForm(Icon applicationIcon)
    {
        Text = "dsh-launcher";
        AccessibleName = "DeepSeek Harness 启动进度";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        ClientSize = new Size(470, 210);
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        _applicationImage = applicationIcon.ToBitmap();
        _titleFont = new Font(Font.FontFamily, 15F, FontStyle.Bold, GraphicsUnit.Point);

        var iconBox = new PictureBox
        {
            AccessibleName = "dsh-launcher",
            Anchor = AnchorStyles.Top,
            Image = _applicationImage,
            Margin = new Padding(0, 4, 20, 0),
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = _titleFont,
            ForeColor = Color.FromArgb(17, 24, 39),
            Margin = new Padding(0),
            Text = "DeepSeek Harness 正在启动",
        };

        var englishLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = new Padding(0, 4, 0, 15),
            Text = "Starting the local web experience...",
        };

        var progressBar = new ProgressBar
        {
            AccessibleName = "启动进度 / Startup progress",
            Dock = DockStyle.Fill,
            Height = 10,
            MarqueeAnimationSpeed = 24,
            Margin = new Padding(0, 0, 0, 12),
            Style = ProgressBarStyle.Marquee,
        };

        var statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(31, 41, 55),
            Margin = new Padding(0),
            Text = "正在等待本地服务响应，请稍候...",
        };

        _hintLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(107, 114, 128),
            Margin = new Padding(0, 5, 0, 0),
            Text = "网页会在服务就绪后自动打开。",
        };

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 5,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.Controls.Add(titleLabel, 0, 0);
        content.Controls.Add(englishLabel, 0, 1);
        content.Controls.Add(progressBar, 0, 2);
        content.Controls.Add(statusLabel, 0, 3);
        content.Controls.Add(_hintLabel, 0, 4);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 28, 26, 24),
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(iconBox, 0, 0);
        layout.Controls.Add(content, 1, 0);
        Controls.Add(layout);

        _longStartTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        _longStartTimer.Tick += HandleLongStart;
        Shown += (_, _) => _longStartTimer.Start();
    }

    private void HandleLongStart(object? sender, EventArgs args)
    {
        _longStartTimer.Stop();
        _hintLabel.Text = "首次启动或更新后可能需要更长时间，请继续等待。";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _longStartTimer.Dispose();
            _titleFont.Dispose();
            _applicationImage.Dispose();
        }

        base.Dispose(disposing);
    }
}
