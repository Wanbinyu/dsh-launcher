using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher;

internal sealed class HarnessInstallProgressForm : Form
{
    private readonly Label _stageLabel;
    private readonly Label _detailLabel;
    private readonly Button _cancelButton;
    private readonly Font _titleFont;
    private bool _completed;

    public HarnessInstallProgressForm(Icon applicationIcon)
    {
        Text = "dsh-launcher - 安装 DeepSeek Harness";
        AccessibleName = "DeepSeek Harness 安装进度";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        ClientSize = new Size(570, 300);
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Icon = applicationIcon;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        _titleFont = new Font(Font.FontFamily, 15F, FontStyle.Bold, GraphicsUnit.Point);
        var title = new Label
        {
            AutoSize = true,
            Font = _titleFont,
            ForeColor = Color.FromArgb(17, 24, 39),
            Margin = new Padding(0, 0, 0, 8),
            Text = "正在准备 DeepSeek Harness",
        };
        var subtitle = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = new Padding(0, 0, 0, 18),
            Text = "Preparing the local Harness environment...",
        };
        var progress = new ProgressBar
        {
            AccessibleName = "安装进度 / Installation progress",
            Dock = DockStyle.Fill,
            Height = 12,
            MarqueeAnimationSpeed = 24,
            Margin = new Padding(0, 0, 0, 16),
            Style = ProgressBarStyle.Marquee,
        };
        _stageLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(31, 41, 55),
            Margin = new Padding(0, 0, 0, 7),
            Text = "正在检查环境 / Checking the environment",
        };
        _detailLabel = new Label
        {
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(107, 114, 128),
            Height = 55,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Text = "请稍候。详细输出同时保存在启动器日志中。",
        };
        _cancelButton = new Button
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            MinimumSize = new Size(110, 36),
            Text = "取消 / Cancel",
            UseVisualStyleBackColor = true,
        };
        _cancelButton.Click += (_, _) => RequestCancellation();

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 16, 0, 0),
        };
        buttonRow.Controls.Add(_cancelButton);

        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 26, 28, 22),
            RowCount = 6,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(title, 0, 0);
        content.Controls.Add(subtitle, 0, 1);
        content.Controls.Add(progress, 0, 2);
        content.Controls.Add(_stageLabel, 0, 3);
        content.Controls.Add(_detailLabel, 0, 4);
        content.Controls.Add(buttonRow, 0, 5);
        Controls.Add(content);
    }

    public event EventHandler? CancellationRequested;

    public void Report(HarnessInstallProgress progress)
    {
        if (_completed || IsDisposed)
        {
            return;
        }

        _stageLabel.Text = progress.Stage;
        _detailLabel.Text = progress.Detail;
    }

    public void MarkCompleted()
    {
        _completed = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (!_completed && args.CloseReason == CloseReason.UserClosing)
        {
            RequestCancellation();
            args.Cancel = true;
            return;
        }

        base.OnFormClosing(args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RequestCancellation()
    {
        if (!_cancelButton.Enabled)
        {
            return;
        }

        _cancelButton.Enabled = false;
        _cancelButton.Text = "正在取消... / Cancelling...";
        _stageLabel.Text = "正在取消安装 / Cancelling installation";
        CancellationRequested?.Invoke(this, EventArgs.Empty);
    }
}
