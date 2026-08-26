using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher;

internal enum HarnessSetupChoice
{
    Cancel,
    Install,
    RunOnce,
    OpenNodeDownload,
}

internal sealed class HarnessSetupPromptForm : Form
{
    private readonly Font _titleFont;

    public HarnessSetupPromptForm(HarnessEnvironmentAssessment assessment, Icon applicationIcon)
    {
        Text = "dsh-launcher - DeepSeek Harness 安装";
        AccessibleName = "DeepSeek Harness 首次安装向导";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        ClientSize = new Size(590, 390);
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;
        Icon = applicationIcon;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        _titleFont = new Font(Font.FontFamily, 16F, FontStyle.Bold, GraphicsUnit.Point);
        var title = new Label
        {
            AutoSize = true,
            Font = _titleFont,
            ForeColor = Color.FromArgb(17, 24, 39),
            Margin = new Padding(0, 0, 0, 8),
            Text = "首次配置 DeepSeek Harness",
        };
        var subtitle = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = new Padding(0, 0, 0, 20),
            Text = "First-time setup for the official @deepseek-ai/dsh npm package",
        };
        var environment = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(31, 41, 55),
            Margin = new Padding(0, 0, 0, 18),
            Text = BuildEnvironmentText(assessment),
        };
        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(530, 0),
            ForeColor = Color.FromArgb(55, 65, 81),
            Margin = new Padding(0),
            Text = BuildExplanation(assessment),
        };

        var primaryButton = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(205, 38),
            Padding = new Padding(10, 0, 10, 0),
            Text = GetPrimaryButtonText(assessment),
            UseVisualStyleBackColor = true,
        };
        primaryButton.Click += (_, _) =>
        {
            Choice = assessment.HasCompatibleNodeAndNpm || assessment.WingetPath is not null
                ? HarnessSetupChoice.Install
                : HarnessSetupChoice.OpenNodeDownload;
            DialogResult = DialogResult.OK;
            Close();
        };

        var runOnceButton = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(155, 38),
            Padding = new Padding(10, 0, 10, 0),
            Text = "仅本次运行 / Run once",
            UseVisualStyleBackColor = true,
            Visible = assessment.HasCompatibleNodeAndNpm && assessment.NpxPath is not null,
        };
        runOnceButton.Click += (_, _) =>
        {
            Choice = HarnessSetupChoice.RunOnce;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            MinimumSize = new Size(90, 38),
            Text = "取消 / Cancel",
            UseVisualStyleBackColor = true,
        };
        cancelButton.Click += (_, _) => Choice = HarnessSetupChoice.Cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 22, 0, 0),
            WrapContents = false,
        };
        buttons.Controls.Add(primaryButton);
        buttons.Controls.Add(runOnceButton);
        buttons.Controls.Add(cancelButton);

        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 26, 28, 22),
            RowCount = 5,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(title, 0, 0);
        content.Controls.Add(subtitle, 0, 1);
        content.Controls.Add(environment, 0, 2);
        content.Controls.Add(explanation, 0, 3);
        content.Controls.Add(buttons, 0, 4);
        Controls.Add(content);

        AcceptButton = primaryButton;
        CancelButton = cancelButton;
    }

    public HarnessSetupChoice Choice { get; private set; } = HarnessSetupChoice.Cancel;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string BuildEnvironmentText(HarnessEnvironmentAssessment assessment)
    {
        var node = assessment.NodePath is null
            ? "未检测到 / not found"
            : $"{assessment.NodePath}（{(assessment.NodeVersion is null ? "版本未知" : "v" + assessment.NodeVersion)}）";
        var npm = assessment.NpmPath is null ? "未检测到 / not found" : assessment.NpmPath;
        return $"环境检查 / Environment check\n\n" +
               $"Node.js：{node}；要求 / required >= {ManagedHarnessInstaller.MinimumNodeVersion}\n" +
               $"npm：{npm}\n" +
               "DeepSeek Harness：尚未安装 / not installed";
    }

    private static string BuildExplanation(HarnessEnvironmentAssessment assessment)
    {
        if (assessment.HasCompatibleNodeAndNpm)
        {
            return "启动器可以把官方 @deepseek-ai/dsh 包安装到当前用户的独立目录，然后自动创建 Web 环境并启动。" +
                   "不会覆盖已有全局包，也不需要访问 GitHub；下载使用当前 npm 配置。\n\n" +
                   "The launcher will install the official package into a per-user managed directory. " +
                   "Existing global installations are not overwritten.";
        }

        if (assessment.WingetPath is not null)
        {
            return "需要先安装 Node.js LTS。点击安装后，启动器会调用 Windows Package Manager；Windows 可能显示权限确认。" +
                   "随后将从 npm 安装官方 @deepseek-ai/dsh 包并继续启动。\n\n" +
                   "Node.js LTS will be installed through winget, followed by the official Harness npm package.";
        }

        return "这台电脑没有 Node.js，也没有可用的 Windows Package Manager。请先从 Node.js 官方网站安装 LTS 版本，" +
               "然后重新双击启动器。\n\nInstall Node.js LTS first, then run dsh-launcher again.";
    }

    private static string GetPrimaryButtonText(HarnessEnvironmentAssessment assessment)
    {
        if (assessment.HasCompatibleNodeAndNpm)
        {
            return "安装 Harness 并启动 / Install & start";
        }

        return assessment.WingetPath is not null
            ? "一键安装环境 / Install requirements"
            : "打开 Node.js 官网 / Open Node.js site";
    }
}
