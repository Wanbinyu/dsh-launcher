using System.Reflection;
using System.Windows.Forms;

namespace DshLauncher;

internal sealed class SponsorForm : Form
{
    private readonly Icon _windowIcon;
    private readonly Image _alipayImage;
    private readonly Image _wechatImage;
    private readonly Font _titleFont;

    public SponsorForm(Icon applicationIcon)
    {
        Text = "赞赏作者 / Support";
        AccessibleName = "赞赏作者 / Support";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        ClientSize = new Size(500, 560);
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(460, 520);
        StartPosition = FormStartPosition.CenterScreen;

        _windowIcon = (Icon)applicationIcon.Clone();
        Icon = _windowIcon;
        _alipayImage = LoadImage("DshLauncher.Assets.sponsor-alipay.png");
        _wechatImage = LoadImage("DshLauncher.Assets.sponsor-wechat.png");
        _titleFont = new Font(Font.FontFamily, 15F, FontStyle.Bold, GraphicsUnit.Point);

        var title = new Label
        {
            AutoSize = true,
            Font = _titleFont,
            ForeColor = Color.FromArgb(17, 24, 39),
            Margin = new Padding(0),
            Text = "感谢支持 dsh-launcher",
        };

        var notice = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = new Padding(0, 5, 0, 16),
            Text = "完全自愿，不影响任何功能。\nVoluntary support only. All features remain available.",
        };

        var tabs = new TabControl
        {
            AccessibleName = "赞赏方式 / Support method",
            Dock = DockStyle.Fill,
            ItemSize = new Size(180, 34),
            Margin = new Padding(0),
            SizeMode = TabSizeMode.Fixed,
        };
        tabs.TabPages.Add(CreatePaymentPage("支付宝 / Alipay", _alipayImage, "支付宝赞赏码"));
        tabs.TabPages.Add(CreatePaymentPage("微信 / WeChat", _wechatImage, "微信赞赏码"));

        var hint = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(107, 114, 128),
            Margin = new Padding(0, 14, 0, 0),
            Text = "请使用对应应用扫码 / Scan with the corresponding app",
            TextAlign = ContentAlignment.MiddleCenter,
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 22, 24, 20),
            RowCount = 4,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(notice, 0, 1);
        layout.Controls.Add(tabs, 0, 2);
        layout.Controls.Add(hint, 0, 3);
        Controls.Add(layout);

        KeyPreview = true;
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    private static TabPage CreatePaymentPage(string title, Image image, string accessibleName)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.White,
            Padding = new Padding(12),
            UseVisualStyleBackColor = false,
        };
        page.Controls.Add(new PictureBox
        {
            AccessibleName = accessibleName,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom,
        });
        return page;
    }

    private static Image LoadImage(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Missing embedded image: {resourceName}");
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _alipayImage.Dispose();
            _wechatImage.Dispose();
            _windowIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
