using System.Text;
using System.Windows.Forms;

namespace DshLauncher;

internal sealed class PluginRecommendationForm : Form
{
    internal static readonly Uri ToolboxUri = new(
        "https://wanbinyu.github.io/wanbinyu-harness-toolbox/");
    internal static readonly Uri CommunityUri = new(
        "https://github.com/deepseek-ai/deepseek-harness/discussions/categories/show-and-tell");

    private readonly PluginRecommendationCatalog _catalog;
    private readonly Action<string> _profileSelected;
    private readonly Action _openHarness;
    private readonly RecommendationInstallInspector? _installInspector;
    private readonly RecommendationSourceHealthChecker? _sourceHealthChecker;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Icon _windowIcon;
    private readonly Font _titleFont;
    private readonly ComboBox _profileCombo;
    private readonly Label _profileSummary;
    private readonly TextBox _searchBox;
    private readonly ComboBox _kindFilterCombo;
    private readonly ComboBox _licenseFilterCombo;
    private readonly CheckBox _hideInstalledCheckBox;
    private readonly Label _catalogStatus;
    private readonly ListView _pluginList;
    private readonly TextBox _pluginDetails;
    private readonly TextBox _commandBox;
    private readonly Button _copyButton;
    private readonly Button _healthButton;
    private IReadOnlyList<PluginRecommendation> _profileItems = Array.Empty<PluginRecommendation>();
    private IReadOnlyDictionary<string, RecommendationInstallStatus> _installStatuses =
        new Dictionary<string, RecommendationInstallStatus>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, RecommendationSourceHealth> _sourceHealth =
        new Dictionary<string, RecommendationSourceHealth>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _checkedItemIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressItemChecked;
    private bool _healthCheckRunning;

    public PluginRecommendationForm(
        PluginRecommendationCatalog catalog,
        string? selectedProfileId,
        Icon applicationIcon,
        Action<string> profileSelected,
        Action openHarness,
        RecommendationInstallInspector? installInspector = null,
        RecommendationSourceHealthChecker? sourceHealthChecker = null)
    {
        _catalog = catalog;
        _profileSelected = profileSelected;
        _openHarness = openHarness;
        _installInspector = installInspector;
        _sourceHealthChecker = sourceHealthChecker;

        Text = "插件与 Skills 推荐 / Plugin & Skills guide";
        AccessibleName = Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 250, 252);
        ClientSize = new Size(1080, 880);
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(900, 740);
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
            Margin = new Padding(0, 8, 0, 10),
        };

        _searchBox = new TextBox
        {
            AccessibleName = "搜索推荐 / Search recommendations",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
            PlaceholderText = "搜索名称、用途、作者或关键词 / Search name, purpose, publisher...",
        };
        _searchBox.TextChanged += (_, _) => RefreshVisibleList();

        _kindFilterCombo = CreateFilterCombo("类型筛选 / Type filter");
        _kindFilterCombo.Items.Add(new FilterOption("all", "全部类型 / All types"));
        _kindFilterCombo.Items.Add(new FilterOption("plugin", "仅插件 / Plugins"));
        _kindFilterCombo.Items.Add(new FilterOption("skill", "仅 Skills / Skills"));
        _kindFilterCombo.SelectedIndexChanged += (_, _) => RefreshVisibleList();
        _kindFilterCombo.SelectedIndex = 0;

        _licenseFilterCombo = CreateFilterCombo("许可筛选 / License filter");
        _licenseFilterCombo.Items.Add(new FilterOption("all", "全部许可 / All licenses"));
        _licenseFilterCombo.Items.Add(new FilterOption("open", "仅开源 / Open source"));
        _licenseFilterCombo.Items.Add(new FilterOption("restricted", "专有或待核验 / Restricted"));
        _licenseFilterCombo.SelectedIndexChanged += (_, _) => RefreshVisibleList();
        _licenseFilterCombo.SelectedIndex = 0;

        _hideInstalledCheckBox = new CheckBox
        {
            AccessibleName = "隐藏已安装插件 / Hide installed plugins",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 3, 0, 0),
            Text = "隐藏已安装插件\nHide installed",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _hideInstalledCheckBox.CheckedChanged += (_, _) => RefreshVisibleList();

        var filters = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
            RowCount = 1,
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23F));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));
        filters.Controls.Add(_searchBox, 0, 0);
        filters.Controls.Add(_kindFilterCombo, 1, 0);
        filters.Controls.Add(_licenseFilterCombo, 2, 0);
        filters.Controls.Add(_hideInstalledCheckBox, 3, 0);

        _catalogStatus = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = new Padding(0, 0, 0, 8),
            Text = _installInspector is null
                ? "安装状态尚未检查；Skills 由 Harness 核验当前工作区。 / Installation status not checked."
                : "打开后将只读检查 Web Profile；Skills 由 Harness 核验当前工作区。 / Read-only check pending.",
        };

        _pluginList = new ListView
        {
            AccessibleName = "推荐插件与 Skills / Recommended plugins and Skills",
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
        _pluginList.Columns.Add("类型与名称 / Type & name", 205);
        _pluginList.Columns.Add("推荐原因 / Why", 280);
        _pluginList.Columns.Add("兼容与要求 / Compatibility", 205);
        _pluginList.Columns.Add("安装状态 / Installed", 145);
        _pluginList.Columns.Add("来源状态 / Source", 130);
        _pluginList.ItemChecked += HandleItemChecked;
        _pluginList.SelectedIndexChanged += (_, _) => UpdateSelectedPluginDetails();

        _pluginDetails = new TextBox
        {
            AccessibleName = "项目双语详情 / Bilingual item details",
            BackColor = Color.FromArgb(239, 246, 255),
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(30, 58, 95),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            TabStop = false,
            WordWrap = true,
        };

        _commandBox = new TextBox
        {
            AccessibleName = "安装请求预览 / Installation request preview",
            BackColor = Color.FromArgb(17, 24, 39),
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(229, 231, 235),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
        };

        var trustNote = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(107, 114, 128),
            Margin = new Padding(0, 8, 0, 0),
            Text = "状态检查只读；目录健康检查仅在点击后联网。项目名和命令是安装标识，不翻译。\n" +
                   "Status checks are read-only. Source health uses the network only when requested.",
        };

        _copyButton = CreateButton("复制安装请求并打开 Harness / Copy & open Harness", HandleCopyInstallationRequest);
        _copyButton.MinimumSize = new Size(280, 38);
        _healthButton = CreateButton("检查目录健康 / Check sources", HandleHealthCheck);
        _healthButton.MinimumSize = new Size(190, 38);
        _healthButton.Enabled = _sourceHealthChecker is not null;
        var openButton = CreateButton("打开所选仓库 / Open repository", HandleOpenRepository);
        openButton.MinimumSize = new Size(190, 38);
        var communityButton = CreateButton("浏览 DSH 社区 / Open community", HandleOpenCommunity);
        communityButton.MinimumSize = new Size(190, 38);
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
        buttons.Controls.Add(_healthButton);
        buttons.Controls.Add(openButton);
        buttons.Controls.Add(communityButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 24, 26, 22),
            RowCount = 12,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(_profileCombo, 0, 2);
        layout.Controls.Add(_profileSummary, 0, 3);
        layout.Controls.Add(filters, 0, 4);
        layout.Controls.Add(_catalogStatus, 0, 5);
        layout.Controls.Add(_pluginList, 0, 6);
        layout.Controls.Add(_pluginDetails, 0, 7);
        layout.Controls.Add(_commandBox, 0, 9);
        layout.Controls.Add(trustNote, 0, 10);
        layout.Controls.Add(buttons, 0, 11);
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
        Shown += async (_, _) => await RefreshInstalledStatusesAsync();

        var selected = _catalog.ResolveProfile(selectedProfileId);
        _profileCombo.SelectedIndex = _catalog.Profiles
            .Select((profile, index) => (profile, index))
            .First(entry => ReferenceEquals(entry.profile, selected)).index;
    }

    internal string SelectedProfileId =>
        (_profileCombo.SelectedItem as ProfileOption)?.Profile.Id ?? _catalog.Profiles[0].Id;

    internal int VisibleItemCount => _pluginList.Items.Count;

    internal int CheckedItemCount => _checkedItemIds.Count;

    internal string InstallationRequestPreview => _commandBox.Text;

    internal string SelectedItemDetails => _pluginDetails.Text;

    internal void SetSearchTextForTest(string text)
    {
        _searchBox.Text = text;
    }

    internal void SetKindFilterForTest(string key)
    {
        SelectFilter(_kindFilterCombo, key);
    }

    internal void SetLicenseFilterForTest(string key)
    {
        SelectFilter(_licenseFilterCombo, key);
    }

    internal void SetHideInstalledForTest(bool value)
    {
        _hideInstalledCheckBox.Checked = value;
    }

    internal void ApplyInstallStatusesForTest(
        IReadOnlyDictionary<string, RecommendationInstallStatus> statuses)
    {
        ApplyInstallStatuses(statuses);
    }

    private static ComboBox CreateFilterCombo(string accessibleName)
    {
        return new ComboBox
        {
            AccessibleName = accessibleName,
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = new Padding(0, 0, 8, 0),
        };
    }

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

    private static void SelectFilter(ComboBox combo, string key)
    {
        combo.SelectedItem = combo.Items.Cast<FilterOption>()
            .First(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
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
        _profileItems = _catalog.ForProfile(profile.Id);
        _checkedItemIds.Clear();
        if (!string.Equals(profile.Id, "complete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in _profileItems.Where(item => !IsInstalledCurrent(item.Id)))
            {
                _checkedItemIds.Add(item.Id);
            }
        }

        RefreshVisibleList();
    }

    private void HandleItemChecked(object? sender, ItemCheckedEventArgs args)
    {
        if (_suppressItemChecked || args.Item.Tag is not PluginRecommendation recommendation)
        {
            return;
        }

        if (args.Item.Checked)
        {
            _checkedItemIds.Add(recommendation.Id);
        }
        else
        {
            _checkedItemIds.Remove(recommendation.Id);
        }

        if (IsHandleCreated)
        {
            BeginInvoke(UpdateCommandPreview);
        }
        else
        {
            UpdateCommandPreview();
        }
    }

    private void RefreshVisibleList()
    {
        if (_pluginList is null || _profileItems is null)
        {
            return;
        }

        var selectedId = _pluginList.SelectedItems.Cast<ListViewItem>()
            .Select(item => (item.Tag as PluginRecommendation)?.Id)
            .FirstOrDefault();
        var visible = _profileItems.Where(MatchesFilters).ToArray();

        _suppressItemChecked = true;
        _pluginList.BeginUpdate();
        try
        {
            _pluginList.Items.Clear();
            foreach (var recommendation in visible)
            {
                var kind = recommendation.IsSkill ? "[Skill]" : "[插件]";
                var versionPrefix = recommendation.IsSkill ? "@" : "v";
                var item = new ListViewItem(
                    $"{kind} {recommendation.Name}  {versionPrefix}{recommendation.Version}")
                {
                    Checked = _checkedItemIds.Contains(recommendation.Id),
                    Tag = recommendation,
                    ToolTipText = BuildToolTip(recommendation),
                };
                item.SubItems.Add($"{recommendation.ReasonZh} / {recommendation.ReasonEn}");
                item.SubItems.Add($"{recommendation.Compatibility}；{recommendation.Requirements}");
                item.SubItems.Add(FormatInstallStatus(recommendation.Id));
                item.SubItems.Add(FormatSourceHealth(recommendation.Id));
                _pluginList.Items.Add(item);
            }
        }
        finally
        {
            _pluginList.EndUpdate();
            _suppressItemChecked = false;
        }

        var selected = _pluginList.Items.Cast<ListViewItem>()
            .FirstOrDefault(item => string.Equals(
                (item.Tag as PluginRecommendation)?.Id,
                selectedId,
                StringComparison.OrdinalIgnoreCase))
            ?? _pluginList.Items.Cast<ListViewItem>().FirstOrDefault();
        if (selected is not null)
        {
            selected.Selected = true;
        }

        UpdateSelectedPluginDetails();
        UpdateCommandPreview();
    }

    private bool MatchesFilters(PluginRecommendation recommendation)
    {
        var kind = (_kindFilterCombo.SelectedItem as FilterOption)?.Key ?? "all";
        if (kind == "plugin" && recommendation.IsSkill ||
            kind == "skill" && !recommendation.IsSkill)
        {
            return false;
        }

        var license = (_licenseFilterCombo.SelectedItem as FilterOption)?.Key ?? "all";
        if (license == "open" && !recommendation.IsOpenSource ||
            license == "restricted" && recommendation.IsOpenSource)
        {
            return false;
        }

        if (_hideInstalledCheckBox.Checked &&
            _installStatuses.TryGetValue(recommendation.Id, out var status) &&
            status.State is RecommendationInstallState.InstalledCurrent or
                RecommendationInstallState.InstalledDifferent)
        {
            return false;
        }

        var search = _searchBox.Text.Trim();
        if (search.Length == 0)
        {
            return true;
        }

        var searchable = string.Join(' ', new[]
        {
            recommendation.Name,
            recommendation.Publisher,
            recommendation.License,
            recommendation.DescriptionZh,
            recommendation.DescriptionEn,
            recommendation.ReasonZh,
            recommendation.ReasonEn,
            recommendation.Requirements,
        });
        return searchable.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildToolTip(PluginRecommendation recommendation)
    {
        return $"{recommendation.DescriptionZh}\n{recommendation.DescriptionEn}\n" +
               $"发布者 / Publisher：{recommendation.Publisher}；许可 / License：{recommendation.License}\n" +
               $"安装 / Installed：{FormatInstallStatus(recommendation.Id)}；" +
               $"来源 / Source：{FormatSourceHealth(recommendation.Id)}\n" +
               $"{recommendation.Privacy}; {recommendation.Network}";
    }

    private void UpdateSelectedPluginDetails()
    {
        var item = _pluginList.SelectedItems.Cast<ListViewItem>().FirstOrDefault()
                   ?? _pluginList.Items.Cast<ListViewItem>().FirstOrDefault();
        if (item?.Tag is not PluginRecommendation recommendation)
        {
            _pluginDetails.Text =
                "没有符合筛选条件的项目。 / No item matches the current filters.";
            return;
        }

        var installDetail = _installStatuses.TryGetValue(recommendation.Id, out var install)
            ? install.Detail
            : null;
        var sourceDetail = _sourceHealth.TryGetValue(recommendation.Id, out var health)
            ? health.Detail
            : null;
        _pluginDetails.Text =
            $"类型 / Type：{(recommendation.IsSkill ? "Skill" : "插件 / Plugin")}    名称 / Name：{recommendation.Name}\r\n" +
            $"发布者 / Publisher：{recommendation.Publisher}    许可 / License：{recommendation.License}\r\n" +
            $"用途：{recommendation.DescriptionZh}\r\n" +
            $"Purpose: {recommendation.DescriptionEn}\r\n" +
            $"推荐原因：{recommendation.ReasonZh}\r\n" +
            $"Why: {recommendation.ReasonEn}\r\n" +
            $"兼容 / Compatibility：{recommendation.Compatibility}\r\n" +
            $"要求 / Requirements：{recommendation.Requirements}\r\n" +
            $"安装状态 / Installed：{FormatInstallStatus(recommendation.Id)}" +
            (string.IsNullOrWhiteSpace(installDetail) ? string.Empty : $" — {installDetail}") + "\r\n" +
            $"来源状态 / Source：{FormatSourceHealth(recommendation.Id)}" +
            (string.IsNullOrWhiteSpace(sourceDetail) ? string.Empty : $" — {sourceDetail}") + "\r\n" +
            $"隐私 / Privacy：{recommendation.Privacy}\r\n" +
            $"联网 / Network：{recommendation.Network}";
    }

    private void UpdateCommandPreview()
    {
        if (IsDisposed)
        {
            return;
        }

        var selectedItems = _profileItems
            .Where(item => _checkedItemIds.Contains(item.Id) && !IsInstalledCurrent(item.Id))
            .ToArray();
        _commandBox.Text = selectedItems.Length == 0
            ? "请选择至少一个尚未安装的插件或 Skill。完整目录默认不勾选。\r\n" +
              "Select at least one item that still needs installation."
            : BuildInstallationRequest(selectedItems);
        _copyButton.Enabled = selectedItems.Length > 0;
    }

    internal static string BuildInstallationRequest(IEnumerable<PluginRecommendation> recommendations)
    {
        var selectedItems = recommendations.ToArray();
        if (selectedItems.Length == 0)
        {
            return string.Empty;
        }

        var request = new StringBuilder();
        request.AppendLine("请帮我安装下面选中的 DeepSeek Harness 插件和 Skills。");
        request.AppendLine();
        request.AppendLine("执行要求：");
        request.AppendLine("1. 先核对当前 DSH 版本、项目来源、许可证和命令，只处理下面列出的项目。");
        request.AppendLine("2. 先检查目标版本是否已经安装；同版本已存在时跳过，并在结果中说明。");
        request.AppendLine("3. 插件使用 dsh plugin --profile web add；Skills 安装到当前工作区 .agents/skills。");
        request.AppendLine("4. Skills 命令已设置 DO_NOT_TRACK=1；如果当前工作区不明确，请先询问我。");
        request.AppendLine("5. 不要读取或修改 API Key、模型密钥、会话以及其他无关配置。");
        request.AppendLine("6. 某项失败时停止该项并说明原因，不要擅自放宽 allowBuilds 或其他安全设置。");
        request.AppendLine("7. 安装完成后汇报每项结果，并提醒我从托盘重启 DSH；不要自行中断当前会话。");

        foreach (var item in selectedItems)
        {
            request.AppendLine();
            request.AppendLine($"[{(item.IsSkill ? "Skill" : "插件")}] {item.Name} ({item.Version})");
            request.AppendLine($"发布者：{item.Publisher}");
            request.AppendLine($"来源：{item.RepositoryUrl}");
            request.AppendLine($"许可证：{item.License}");
            request.AppendLine($"环境要求：{item.Requirements}");
            request.AppendLine($"命令：{item.InstallCommand}");
        }

        return request.ToString().TrimEnd();
    }

    internal static void CopyInstallationRequest(
        string request,
        Action<string> copyText,
        Action openHarness)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            throw new ArgumentException("The installation request cannot be empty.", nameof(request));
        }

        copyText(request);
        openHarness();
    }

    private async Task RefreshInstalledStatusesAsync()
    {
        if (_installInspector is null || IsDisposed)
        {
            return;
        }

        _catalogStatus.Text =
            "正在只读检查 Web Profile 插件... / Checking Web Profile plugins read-only...";
        try
        {
            var statuses = await _installInspector.InspectAsync(
                _catalog.Items,
                _lifetimeCancellation.Token);
            if (IsDisposed)
            {
                return;
            }

            ApplyInstallStatuses(statuses);
            var installed = statuses.Values.Count(status => status.State is
                RecommendationInstallState.InstalledCurrent or
                RecommendationInstallState.InstalledDifferent);
            _catalogStatus.Text =
                $"已只读识别 {installed} 个目录插件；Skills 由 Harness 核验当前工作区。 / " +
                $"Detected {installed} catalog plugins; Harness checks workspace Skills.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _catalogStatus.Text =
                $"无法检查安装状态：{exception.Message} / Installation check failed.";
        }
    }

    private void ApplyInstallStatuses(
        IReadOnlyDictionary<string, RecommendationInstallStatus> statuses)
    {
        _installStatuses = statuses;
        foreach (var installedId in statuses
                     .Where(entry => entry.Value.State == RecommendationInstallState.InstalledCurrent)
                     .Select(entry => entry.Key))
        {
            _checkedItemIds.Remove(installedId);
        }

        RefreshVisibleList();
    }

    private async void HandleHealthCheck(object? sender, EventArgs args)
    {
        if (_sourceHealthChecker is null || _healthCheckRunning)
        {
            return;
        }

        _healthCheckRunning = true;
        _healthButton.Enabled = false;
        var progress = new Progress<RecommendationHealthProgress>(state =>
        {
            if (IsDisposed)
            {
                return;
            }

            _healthButton.Text = $"检查中 {state.Completed}/{state.Total} / Checking";
            _catalogStatus.Text =
                $"正在按用户请求检查目录来源：{state.Completed}/{state.Total}。不会上传选择。 / Checking sources on request.";
        });
        try
        {
            _sourceHealth = await _sourceHealthChecker.CheckAsync(
                _catalog.Items,
                progress,
                _lifetimeCancellation.Token);
            if (IsDisposed)
            {
                return;
            }

            var available = _sourceHealth.Values.Count(result =>
                result.State == RecommendationSourceHealthState.Available);
            var warnings = _sourceHealth.Values.Count(result =>
                result.State == RecommendationSourceHealthState.Warning);
            var unavailable = _sourceHealth.Values.Count(result =>
                result.State == RecommendationSourceHealthState.Unavailable);
            var checkedAt = _sourceHealth.Values.Max(result => result.CheckedAtUtc).ToLocalTime();
            _catalogStatus.Text =
                $"来源核验：可用 {available}，警告 {warnings}，无法核验 {unavailable}；" +
                $"{checkedAt:yyyy-MM-dd HH:mm:ss}。网络失败不等于项目失效。 / Source check complete.";
            RefreshVisibleList();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"目录健康检查失败 / Source check failed:\n{exception.Message}",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _healthCheckRunning = false;
            if (!IsDisposed)
            {
                _healthButton.Text = "检查目录健康 / Check sources";
                _healthButton.Enabled = true;
            }
        }
    }

    private bool IsInstalledCurrent(string itemId)
    {
        return _installStatuses.TryGetValue(itemId, out var status) &&
               status.State == RecommendationInstallState.InstalledCurrent;
    }

    private string FormatInstallStatus(string itemId)
    {
        if (!_installStatuses.TryGetValue(itemId, out var status))
        {
            return "待检查 / Pending";
        }

        return status.State switch
        {
            RecommendationInstallState.WorkspaceDependent => "Harness 核验 / Harness",
            RecommendationInstallState.NotInstalled => "未安装 / Missing",
            RecommendationInstallState.InstalledCurrent =>
                $"已安装 {status.InstalledVersion} / Current",
            RecommendationInstallState.InstalledDifferent =>
                $"已有 {status.InstalledVersion} / Different",
            _ => "无法判断 / Unknown",
        };
    }

    private string FormatSourceHealth(string itemId)
    {
        if (!_sourceHealth.TryGetValue(itemId, out var health))
        {
            return "未检查 / Unchecked";
        }

        return health.State switch
        {
            RecommendationSourceHealthState.Available => "可用 / Available",
            RecommendationSourceHealthState.Warning => "警告 / Warning",
            RecommendationSourceHealthState.Unavailable => "无法核验 / Unavailable",
            _ => "未检查 / Unchecked",
        };
    }

    private void HandleCopyInstallationRequest(object? sender, EventArgs args)
    {
        if (!_copyButton.Enabled)
        {
            return;
        }

        try
        {
            CopyInstallationRequest(_commandBox.Text, Clipboard.SetText, _openHarness);
            MessageBox.Show(
                "安装请求已复制，DeepSeek Harness 正在打开。\n" +
                "请在输入框按 Ctrl+V，检查内容后发送。\n\n" +
                "Request copied. Paste it into Harness, review it, and send.",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法复制安装请求或打开 Harness / Could not copy or open Harness:\n{exception.Message}",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void HandleOpenRepository(object? sender, EventArgs args)
    {
        var item = _pluginList.SelectedItems.Cast<ListViewItem>().FirstOrDefault()
                   ?? _pluginList.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Checked);
        if (item?.Tag is PluginRecommendation recommendation)
        {
            OpenUri(new Uri(recommendation.RepositoryUrl));
        }
    }

    private void HandleOpenCommunity(object? sender, EventArgs args)
    {
        OpenUri(CommunityUri);
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
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
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

    private sealed class FilterOption
    {
        public FilterOption(string key, string text)
        {
            Key = key;
            Text = text;
        }

        public string Key { get; }

        public string Text { get; }

        public override string ToString()
        {
            return Text;
        }
    }
}
