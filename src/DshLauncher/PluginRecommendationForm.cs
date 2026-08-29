using System.Windows.Forms;

namespace DshLauncher;

internal sealed class PluginRecommendationForm : Form
{
    internal static readonly Uri ToolboxUri = new(
        "https://wanbinyu.github.io/wanbinyu-harness-toolbox/");

    private readonly PluginRecommendationCatalog _catalog;
    private readonly Action<string> _profileSelected;
    private readonly Icon _windowIcon;
    private readonly Font _titleFont;
    private readonly ComboBox _profileCombo;
    private readonly Label _profileSummary;
    private readonly ListView _pluginList;
    private readonly Label _pluginDetails;
    private readonly TextBox _commandBox;
    private readonly Button _copyButton;
    private IReadOnlyList<PluginRecommendation> _visiblePlugins = Array.Empty<PluginRecommendation>();

    public PluginRecommendationForm(
        PluginRecommendationCatalog catalog,
        string? selectedProfileId,
        Icon applicationIcon,
        Action<string> profileSelected)
    {
        _catalog = catalog;
        _profileSelected = profileSelected;

        Text = "插件与 Skills 推荐 / Plugin & Skills guide";
        AccessibleName = Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        ClientSize = new Size(880, 650);
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _windowIcon = (Icon)applicationIcon.Clone();
        Icon = _windowIcon;
        _titleFont = new Font(Font.FontFamily, 16F, FontStyle.Bold, GraphicsUnit.Point);

        var title = new Label
        {
            AutoSize = true,
            Font = _titleFont,
            ForeColor = Color.FromArgb(17, 24, 39),
            Margin = new Padding(0),
            Text = "你主要用 DeepSeek Harness 做什么？",
        };
        var subtitle = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = new Padding(0, 5, 0, 16),
            Text = "选择方向后给出本地规则推荐。不会读取会话、文件或密钥，也不会上传选择。\n" +
                   "Choose a workflow to get local, private recommendations.",
        };

        _profileCombo = new ComboBox
        {
            AccessibleName = "使用方向 / Workflow",
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = new Padding(0),
        };
        foreach (var profile in _catalog.Profiles)
        {
            _profileCombo.Items.Add(new ProfileOption(profile));
        }
        _profileCombo.SelectedIndexChanged += HandleProfileChanged;

        _profileSummary = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(55, 65, 81),
            Margin = new Padding(0, 8, 0, 14),
        };

        _pluginList = new ListView
        {
            AccessibleName = "推荐插件 / Recommended plugins",
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CheckBoxes = true,
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false,
            ShowItemToolTips = true,
            View = View.Details,
        };
        _pluginList.Columns.Add("插件 / Plugin", 190);
        _pluginList.Columns.Add("推荐原因 / Why", 350);
        _pluginList.Columns.Add("兼容性 / Compatibility", 260);
        _pluginList.ItemChecked += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(UpdateCommandPreview);
            }
        };
        _pluginList.SelectedIndexChanged += (_, _) => UpdateSelectedPluginDetails();

        _pluginDetails = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            BackColor = Color.FromArgb(239, 246, 255),
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(30, 58, 95),
            Padding = new Padding(9, 6, 9, 6),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _commandBox = new TextBox
        {
            AccessibleName = "安装命令预览 / Install command preview",
            BackColor = Color.FromArgb(17, 24, 39),
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(229, 231, 235),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
        };

        var trustNote = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(107, 114, 128),
            Margin = new Padding(0, 8, 0, 0),
            Text = "当前只列出带 bundle 清单和固定 Release 地址的已验证插件；未经验证的仓库不会当作 DSH Skills 推荐。\n" +
                   "Nothing is installed automatically. Review each repository before running copied commands.",
        };

        _copyButton = CreateButton("复制已选安装命令 / Copy commands", HandleCopyCommands);
        _copyButton.MinimumSize = new Size(205, 38);
        var openButton = CreateButton("打开所选仓库 / Open repository", HandleOpenRepository);
        openButton.MinimumSize = new Size(190, 38);
        var toolboxButton = CreateButton("浏览工具箱 / Open toolbox", HandleOpenToolbox);
        toolboxButton.MinimumSize = new Size(170, 38);
        var closeButton = CreateButton("完成 / Done", (_, _) => Close());
        closeButton.MinimumSize = new Size(100, 38);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 16, 0, 0),
            WrapContents = true,
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_copyButton);
        buttons.Controls.Add(openButton);
        buttons.Controls.Add(toolboxButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 24, 26, 22),
            RowCount = 10,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(_profileCombo, 0, 2);
        layout.Controls.Add(_profileSummary, 0, 3);
        layout.Controls.Add(_pluginList, 0, 4);
        layout.Controls.Add(_pluginDetails, 0, 5);
        layout.Controls.Add(_commandBox, 0, 7);
        layout.Controls.Add(trustNote, 0, 8);
        layout.Controls.Add(buttons, 0, 9);
        Controls.Add(layout);

        CancelButton = closeButton;
        KeyPreview = true;
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        var selected = _catalog.ResolveProfile(selectedProfileId);
        _profileCombo.SelectedIndex = _catalog.Profiles
            .Select((profile, index) => (profile, index))
            .First(entry => ReferenceEquals(entry.profile, selected)).index;
    }

    internal string SelectedProfileId =>
        (_profileCombo.SelectedItem as ProfileOption)?.Profile.Id ?? _catalog.Profiles[0].Id;

    internal int VisiblePluginCount => _pluginList.Items.Count;

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(8, 0, 8, 0),
            Text = text,
            UseVisualStyleBackColor = true,
        };
        button.Click += handler;
        return button;
    }

    private void HandleProfileChanged(object? sender, EventArgs args)
    {
        if (_profileCombo.SelectedItem is not ProfileOption option)
        {
            return;
        }

        var profile = option.Profile;
        _profileSelected(profile.Id);
        _profileSummary.Text = $"{profile.SummaryZh}\n{profile.SummaryEn}";
        _visiblePlugins = _catalog.ForProfile(profile.Id);

        _pluginList.BeginUpdate();
        try
        {
            _pluginList.Items.Clear();
            foreach (var plugin in _visiblePlugins)
            {
                var item = new ListViewItem($"{plugin.Name}  v{plugin.Version}")
                {
                    Checked = true,
                    Tag = plugin,
                    ToolTipText = $"{plugin.DescriptionZh}\n{plugin.Privacy}; {plugin.Network}",
                };
                item.SubItems.Add(plugin.ReasonZh);
                item.SubItems.Add(plugin.Compatibility);
                _pluginList.Items.Add(item);
            }
        }
        finally
        {
            _pluginList.EndUpdate();
        }

        if (_pluginList.Items.Count > 0)
        {
            _pluginList.Items[0].Selected = true;
        }
        UpdateSelectedPluginDetails();
        UpdateCommandPreview();
    }

    private void UpdateSelectedPluginDetails()
    {
        var item = _pluginList.SelectedItems.Cast<ListViewItem>().FirstOrDefault()
                   ?? _pluginList.Items.Cast<ListViewItem>().FirstOrDefault();
        _pluginDetails.Text = item?.Tag is not PluginRecommendation plugin
            ? "选择插件后可查看用途、隐私与联网边界。 / Select a plugin to inspect its boundaries."
            : $"用途 / Purpose：{plugin.DescriptionZh}\n" +
              $"隐私 / Privacy：{plugin.Privacy}    联网 / Network：{plugin.Network}";
    }

    private void UpdateCommandPreview()
    {
        if (IsDisposed)
        {
            return;
        }

        var commands = _pluginList.Items.Cast<ListViewItem>()
            .Where(item => item.Checked)
            .Select(item => ((PluginRecommendation)item.Tag!).InstallCommand)
            .ToList();
        if (commands.Count > 0)
        {
            commands.Add("dsh restart");
        }

        _commandBox.Text = commands.Count == 0
            ? "请选择至少一个插件。 / Select at least one plugin."
            : string.Join(Environment.NewLine, commands);
        _copyButton.Enabled = commands.Count > 0;
    }

    private void HandleCopyCommands(object? sender, EventArgs args)
    {
        if (!_copyButton.Enabled)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_commandBox.Text);
            MessageBox.Show(
                "安装命令已复制。请在新终端中检查后运行。\n\nCommands copied. Review them before running.",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法复制安装命令 / Could not copy commands:\n{exception.Message}",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void HandleOpenRepository(object? sender, EventArgs args)
    {
        var item = _pluginList.SelectedItems.Cast<ListViewItem>().FirstOrDefault()
                   ?? _pluginList.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Checked);
        if (item?.Tag is PluginRecommendation plugin)
        {
            OpenUri(new Uri(plugin.RepositoryUrl));
        }
    }

    private void HandleOpenToolbox(object? sender, EventArgs args)
    {
        OpenUri(ToolboxUri);
    }

    private static void OpenUri(Uri uri)
    {
        try
        {
            BrowserLauncher.Open(uri);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法打开页面 / Could not open the page:\n{exception.Message}",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commandBox.Font.Dispose();
            _titleFont.Dispose();
            _windowIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class ProfileOption
    {
        public ProfileOption(RecommendationProfile profile)
        {
            Profile = profile;
        }

        public RecommendationProfile Profile { get; }

        public override string ToString()
        {
            return $"{Profile.NameZh} / {Profile.NameEn}";
        }
    }
}
