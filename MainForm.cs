using System.Drawing;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;

namespace RestartWindowsService;

internal sealed class MainForm : Form
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private const int LogTailMaxLines = 200;
    private const int LogTailReadMaxBytes = 512 * 1024;
    private const int LogTextBoxMaxLength = 50000;

    private static readonly Color PageBackground = Color.FromArgb(245, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color Primary = Color.FromArgb(15, 99, 198);
    private static readonly Color PrimaryHover = Color.FromArgb(12, 84, 170);
    private static readonly Color TextPrimary = Color.FromArgb(33, 37, 41);
    private static readonly Color TextMuted = Color.FromArgb(90, 90, 90);
    private static readonly Color Success = Color.FromArgb(46, 125, 50);
    private static readonly Color Warning = Color.FromArgb(181, 71, 0);
    private static readonly Color Danger = Color.FromArgb(183, 28, 28);

    private readonly ServiceManager _serviceManager;
    private readonly string _serviceDisplayName;
    private readonly bool _autoStart;

    private readonly Label _statusValue;
    private readonly Label _adminValue;
    private readonly Label _messageValue;
    private readonly Panel _messagePanel;
    private readonly TextBox _logTextBox;
    private readonly Button _pickLogButton;
    private readonly Label _logPathLabel;

    private readonly Button _startButton;
    private readonly Button _restartButton;
    private readonly Button _stopButton;
    private readonly Button _actionButton;
    private readonly Control _logPanel;

    private readonly Panel _contentPanel;
    private readonly Control _mainPage;
    private readonly Control _actionPage;
    private bool _isActionPageVisible;

    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _logTimer;

    private bool _isBusy;
    private bool _isExiting;
    private NotifyIcon? _trayIcon;
    private string? _logFilePath;
    private long _logLastLength = -1;
    private DateTime _logLastWriteTimeUtc = DateTime.MinValue;
    private bool _isLogReading;

    public MainForm(string serviceName, string serviceDisplayName, bool autoStart)
    {
        _serviceManager = new ServiceManager(serviceName);
        _serviceDisplayName = serviceDisplayName;
        _autoStart = autoStart;

        Text = $"{_serviceDisplayName} 控制";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(912, 640);
        MinimumSize = new Size(912, 600);
        BackColor = PageBackground;
        DoubleBuffered = true;

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(18),
            BackColor = BackColor,
        };
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };
        card.Paint += (_, e) =>
        {
            var rect = card.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            using var pen = new Pen(Color.FromArgb(225, 230, 238));
            e.Graphics.DrawRectangle(pen, rect);
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        var header = BuildHeader(_serviceDisplayName, _serviceManager.ServiceName);
        var footer = BuildFooter(out _startButton, out _restartButton, out _stopButton, out _actionButton, out _pickLogButton, out _logPathLabel, out _logPanel);

        // 内容面板 - 用于切换页面
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        // 主页面
        _mainPage = BuildMainPage(out _statusValue, out _adminValue, out _messagePanel, out _messageValue, out _logTextBox);
        _mainPage.Dock = DockStyle.Fill;

        // 操作页面（从单独文件加载）
        _actionPage = new ActionPage();
        _actionPage.Visible = false;

        _contentPanel.Controls.Add(_mainPage);
        _contentPanel.Controls.Add(_actionPage);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_contentPanel, 0, 1);
        root.Controls.Add(footer, 0, 2);

        card.Controls.Add(root);
        page.Controls.Add(card, 0, 0);
        Controls.Add(page);
        _messagePanel.Resize += (_, _) => UpdateMessageWrapWidth();
        UpdateMessageWrapWidth();

        _startButton.Click += async (_, _) => await StartServiceAsync();
        _restartButton.Click += async (_, _) => await RestartServiceAsync();
        _stopButton.Click += async (_, _) => await StopServiceAsync();
        _pickLogButton.Click += (_, _) => PickLogFile();
        _actionButton.Click += (_, _) => ToggleActionPage();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _refreshTimer.Tick += (_, _) => RefreshUi();

        _logTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _logTimer.Tick += (_, _) => _ = RefreshLogTailAsync();

        Shown += async (_, _) =>
        {
            EnsureTrayIcon();
            _refreshTimer.Start();
            RefreshUi();

            if (_autoStart)
            {
                await StartServiceAsync();
            }
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        };

        FormClosing += (_, e) =>
        {
            if (_isExiting)
            {
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        };
    }

    private static Control BuildHeader(string serviceDisplayName, string serviceName)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Primary,
            Padding = new Padding(16, 14, 16, 14),
            MinimumSize = new Size(0, 74),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Text = $"{serviceDisplayName} 控制面板",
            ForeColor = Color.White,
            Font = new Font(Control.DefaultFont.FontFamily, 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Text = $"服务名：{serviceName}",
            ForeColor = Color.FromArgb(230, 240, 255),
            Padding = new Padding(0, 3, 0, 0),
        };

        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(subtitle, 0, 1);

        var badge = new Label
        {
            AutoSize = true,
            Text = "Windows 服务",
            ForeColor = Color.White,
            BackColor = Color.FromArgb(35, 255, 255, 255),
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(12, 0, 0, 0),
        };

        layout.Controls.Add(titleStack, 0, 0);
        layout.Controls.Add(badge, 1, 0);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildMainPage(out Label statusValue, out Label adminValue, out Panel messagePanel, out Label messageValue, out TextBox logTextBox)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CardBackground,
            Padding = new Padding(16, 16, 16, 12),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var infoCard = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(250, 251, 253),
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        infoCard.Paint += (_, e) =>
        {
            var rect = infoCard.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            using var pen = new Pen(Color.FromArgb(231, 236, 244));
            e.Graphics.DrawRectangle(pen, rect);
        };

        var infoGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(infoGrid, 0, "服务名称", new Label
        {
            AutoSize = true,
            Text = _serviceDisplayName,
            Font = new Font(Control.DefaultFont.FontFamily, 12.5f, FontStyle.Bold),
            ForeColor = Primary,
            MaximumSize = new Size(520, 0),
            AutoEllipsis = true,
        }, isKey: true);

        AddRow(infoGrid, 1, "服务名", new Label
        {
            AutoSize = true,
            Text = _serviceManager.ServiceName,
            ForeColor = TextPrimary,
            MaximumSize = new Size(520, 0),
            AutoEllipsis = true,
        });

        statusValue = new Label
        {
            AutoSize = true,
            Text = "—",
            ForeColor = TextPrimary,
            Font = new Font(Control.DefaultFont.FontFamily, 11f, FontStyle.Bold),
        };
        AddRow(infoGrid, 2, "运行状态", statusValue);

        adminValue = new Label
        {
            AutoSize = true,
            Text = "—",
            Font = new Font(Control.DefaultFont.FontFamily, 11f, FontStyle.Bold),
        };
        AddRow(infoGrid, 3, "管理员", adminValue);

        var hint = new Label
        {
            AutoSize = true,
            Text = "提示：关闭窗口会缩到托盘运行，不会停止服务。",
            ForeColor = TextMuted,
            Padding = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(560, 0),
        };
        infoGrid.Controls.Add(hint, 1, 4);

        infoCard.Controls.Add(infoGrid);

        var msgPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 247, 250),
            AutoScroll = true,
            Padding = new Padding(14),
            Margin = new Padding(0),
        };
        msgPanel.Paint += (_, e) =>
        {
            var rect = msgPanel.ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            using var pen = new Pen(Color.FromArgb(231, 236, 244));
            e.Graphics.DrawRectangle(pen, rect);
        };

        messagePanel = msgPanel;

        messageValue = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = "",
            ForeColor = TextMuted,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };

        var logHeader = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = "日志（tail）",
            ForeColor = TextMuted,
            Padding = new Padding(0, 12, 0, 6),
            Margin = new Padding(0),
        };

        logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
            MaxLength = LogTextBoxMaxLength * 2,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(messageValue, 0, 0);
        layout.Controls.Add(logHeader, 0, 1);
        layout.Controls.Add(logTextBox, 0, 2);

        msgPanel.Controls.Add(layout);

        outer.Controls.Add(infoCard, 0, 0);
        outer.Controls.Add(msgPanel, 0, 1);

        return outer;
    }



    private void ToggleActionPage()
    {
        _isActionPageVisible = !_isActionPageVisible;

        if (_isActionPageVisible)
        {
            _mainPage.Visible = false;
            _actionPage.Visible = true;
            _actionButton.Text = "返回";

            // 隐藏服务操作按钮和日志区域
            _startButton.Visible = false;
            _restartButton.Visible = false;
            _stopButton.Visible = false;
            _pickLogButton.Visible = false;
            _logPanel.Visible = false;

            // 暂停主页面相关的定时器
            _refreshTimer?.Stop();
            _logTimer?.Stop();
        }
        else
        {
            _actionPage.Visible = false;
            _mainPage.Visible = true;
            _actionButton.Text = "操作";

            // 显示服务操作按钮和日志区域
            _startButton.Visible = true;
            _restartButton.Visible = true;
            _stopButton.Visible = true;
            _pickLogButton.Visible = true;
            _logPanel.Visible = true;

            // 恢复主页面相关的定时器
            _refreshTimer?.Start();
            _logTimer?.Start();
            RefreshUi();
        }
    }

    private void UpdateMessageWrapWidth()
    {
        var availableWidth = _messagePanel.ClientSize.Width - _messagePanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4;
        if (availableWidth < 0)
        {
            availableWidth = 0;
        }

        _messageValue.MaximumSize = new Size(availableWidth, 0);
        _messageValue.AutoEllipsis = false;
        _messagePanel.PerformLayout();
    }

    private static void AddRow(TableLayoutPanel grid, int rowIndex, string labelText, Control valueControl, bool isKey = false)
    {
        if (grid.RowStyles.Count <= rowIndex)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            ForeColor = isKey ? TextMuted : TextMuted,
            Padding = new Padding(0, 6, 0, 6),
        };

        valueControl.Padding = new Padding(0, 6, 0, 6);

        grid.Controls.Add(label, 0, rowIndex);
        grid.Controls.Add(valueControl, 1, rowIndex);
    }

    private static Control BuildFooter(out Button startButton, out Button restartButton, out Button stopButton, out Button actionButton, out Button pickLogButton, out Label logPathLabel, out Control logPanel)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 10, 16, 14),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var stop = new Button
        {
            Text = "停止",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(72, 35),
            Padding = new Padding(12, 7, 12, 7),
        };

        var restart = new Button
        {
            Text = "重启",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(72, 35),
            Padding = new Padding(12, 7, 12, 7),
        };

        var start = new Button
        {
            Text = "启动",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(72, 35),
            Padding = new Padding(12, 7, 12, 7),
        };

        var action = new Button
        {
            Text = "操作",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(72, 35),
            Padding = new Padding(12, 7, 12, 7),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };
        buttons.Padding = new Padding(0, 2, 0, 0);
        buttons.Controls.Add(stop);
        buttons.Controls.Add(restart);
        buttons.Controls.Add(start);
        buttons.Controls.Add(action);
        buttons.SetFlowBreak(action, false);

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0, 8, 0, 0),
        };

        var logLabel = new Label
        {
            AutoSize = true,
            Text = "日志：",
            ForeColor = TextMuted,
            Padding = new Padding(0, 6, 0, 0),
            Margin = new Padding(0, 0, 8, 0),
        };

        logPathLabel = new Label
        {
            AutoSize = false,
            Text = "未选择",
            ForeColor = TextPrimary,
            Width = 360,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 10, 0),
        };

        pickLogButton = new Button
        {
            Text = "选择日志…",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(72, 35),
            Padding = new Padding(12, 7, 12, 7),
        };

        ApplyButtonStyle(pickLogButton, ButtonVisualKind.Secondary);

        left.Controls.Add(logLabel);
        left.Controls.Add(logPathLabel);

        // 将选择日志按钮放在右侧按钮组，与其他按钮视觉统一
        pickLogButton.Margin = new Padding(0, 0, 12, 0);
        buttons.Controls.Add(pickLogButton);

        panel.Controls.Add(left, 0, 0);
        panel.Controls.Add(buttons, 1, 0);

        ApplyButtonStyle(start, ButtonVisualKind.Primary);
        ApplyButtonStyle(restart, ButtonVisualKind.Secondary);
        ApplyButtonStyle(stop, ButtonVisualKind.Danger);
        ApplyButtonStyle(action, ButtonVisualKind.Secondary);

        stopButton = stop;
        restartButton = restart;
        startButton = start;
        actionButton = action;
        logPanel = left;

        return panel;
    }

    private enum ButtonVisualKind
    {
        Primary,
        Secondary,
        Danger,
    }

    private static void ApplyButtonStyle(Button button, ButtonVisualKind kind)
    {
        Color normalBack;
        Color hoverBack;
        Color normalFore;
        Color border;
        Color disabledBack;
        Color disabledFore;
        Color disabledBorder;

        switch (kind)
        {
            case ButtonVisualKind.Primary:
                normalBack = Primary;
                hoverBack = PrimaryHover;
                normalFore = Color.White;
                border = Primary;
                disabledBack = Color.FromArgb(196, 215, 242);
                disabledFore = Color.White;
                disabledBorder = disabledBack;
                break;
            case ButtonVisualKind.Danger:
                normalBack = Color.FromArgb(253, 240, 240);
                hoverBack = Color.FromArgb(251, 226, 226);
                normalFore = Danger;
                border = Color.FromArgb(246, 204, 204);
                disabledBack = Color.FromArgb(246, 246, 246);
                disabledFore = Color.FromArgb(160, 160, 160);
                disabledBorder = Color.FromArgb(232, 232, 232);
                break;
            default:
                normalBack = Color.White;
                hoverBack = Color.FromArgb(245, 247, 250);
                normalFore = TextPrimary;
                border = Color.FromArgb(220, 226, 236);
                disabledBack = Color.FromArgb(246, 246, 246);
                disabledFore = Color.FromArgb(160, 160, 160);
                disabledBorder = Color.FromArgb(232, 232, 232);
                break;
        }

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = border;
        button.BackColor = normalBack;
        button.ForeColor = normalFore;
        button.Cursor = Cursors.Hand;
        button.Font = new Font(Control.DefaultFont.FontFamily, 10f, FontStyle.Bold);
        button.Margin = new Padding(10, 0, 0, 0);

        void SyncEnabledStyle()
        {
            if (!button.Enabled)
            {
                button.BackColor = disabledBack;
                button.ForeColor = disabledFore;
                button.FlatAppearance.BorderColor = disabledBorder;
                button.Cursor = Cursors.Default;
                return;
            }

            button.BackColor = normalBack;
            button.ForeColor = normalFore;
            button.FlatAppearance.BorderColor = border;
            button.Cursor = Cursors.Hand;
        }

        button.EnabledChanged += (_, _) => SyncEnabledStyle();
        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = hoverBack;
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = normalBack;
            }
        };

        SyncEnabledStyle();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
        {
            return;
        }

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("打开");
        openItem.Click += (_, _) => ShowFromTray();

        var startItem = new ToolStripMenuItem("启动");
        startItem.Click += async (_, _) => await StartServiceAsync();

        var restartItem = new ToolStripMenuItem("重启");
        restartItem.Click += async (_, _) => await RestartServiceAsync();

        var stopItem = new ToolStripMenuItem("停止");
        stopItem.Click += async (_, _) => await StopServiceAsync();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            _isExiting = true;
            _trayIcon!.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
            Application.Exit();
        };

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startItem);
        menu.Items.Add(restartItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Text = _serviceDisplayName,
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        Hide();
        _trayIcon?.ShowBalloonTip(1500, "已缩到托盘", "程序在后台运行，不会停止服务。", ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshUi();
    }

    private void RefreshUi()
    {
        var snapshot = _serviceManager.GetSnapshot();
        if (!snapshot.Exists)
        {
            _statusValue.Text = "未找到服务";
            _statusValue.ForeColor = Danger;
            SetMessage(snapshot.ErrorMessage ?? "未找到服务或无法读取服务状态。", isError: true);
            SetButtonsEnabled(false, false, false);
            return;
        }

        var statusText = snapshot.Status switch
        {
            ServiceControllerStatus.Running => "运行中",
            ServiceControllerStatus.Stopped => "已停止",
            ServiceControllerStatus.Paused => "已暂停",
            ServiceControllerStatus.StartPending => "正在启动",
            ServiceControllerStatus.StopPending => "正在停止",
            ServiceControllerStatus.ContinuePending => "正在继续",
            ServiceControllerStatus.PausePending => "正在暂停",
            _ => snapshot.Status?.ToString() ?? "未知",
        };

        _statusValue.Text = statusText;
        _statusValue.ForeColor = snapshot.Status == ServiceControllerStatus.Running
            ? Success
            : TextMuted;

        var isAdministrator = IsAdministrator();
        _adminValue.Text = isAdministrator ? "是" : "否";
        _adminValue.ForeColor = isAdministrator ? Success : Warning;

        if (!_isBusy && (string.IsNullOrWhiteSpace(_messageValue.Text) || _messageValue.Text == "就绪。"))
        {
            SetMessage(isAdministrator ? "就绪。" : "当前不是管理员权限，点击按钮可能会失败（建议右键以管理员身份运行）。", isError: false);
        }

        if (_isBusy)
        {
            SetButtonsEnabled(false, false, false);
            return;
        }

        var status = snapshot.Status;
        var startEnabled = status is null || (status != ServiceControllerStatus.Running && status != ServiceControllerStatus.StartPending);
        var stopEnabled = status is null || (status != ServiceControllerStatus.Stopped && status != ServiceControllerStatus.StopPending);
        var restartEnabled = status is null || (status != ServiceControllerStatus.StartPending && status != ServiceControllerStatus.StopPending);

        SetButtonsEnabled(startEnabled, restartEnabled, stopEnabled);
    }

    private void SetButtonsEnabled(bool startEnabled, bool restartEnabled, bool stopEnabled)
    {
        _startButton.Enabled = startEnabled;
        _restartButton.Enabled = restartEnabled;
        _stopButton.Enabled = stopEnabled;
    }

    private void SetMessage(string message, bool isError)
    {
        _messageValue.Text = message;
        _messageValue.ForeColor = isError ? Danger : TextMuted;
        _messagePanel.BackColor = isError ? Color.FromArgb(255, 235, 238) : Color.FromArgb(245, 247, 250);
        UpdateMessageWrapWidth();
    }

    private void PickLogFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择日志文件",
            Filter = "日志文件 (*.log;*.txt)|*.log;*.txt|所有文件 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(_logFilePath))
        {
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                dialog.InitialDirectory = dir;
            }
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _logFilePath = dialog.FileName;
        _logPathLabel.Text = _logFilePath;
        _logLastLength = -1;
        _logLastWriteTimeUtc = DateTime.MinValue;
        _logTextBox.Text = "";
        _logTimer.Start();
        _ = RefreshLogTailAsync(force: true);
    }

    private async Task RefreshLogTailAsync(bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        if (_isLogReading)
        {
            return;
        }

        _isLogReading = true;
        try
        {
            var fileInfo = new FileInfo(_logFilePath);
            if (!fileInfo.Exists)
            {
                _logTextBox.Text = $"日志文件不存在：{_logFilePath}";
                _logTimer.Stop();
                return;
            }

            var length = fileInfo.Length;
            var writeTimeUtc = fileInfo.LastWriteTimeUtc;
            if (!force && length == _logLastLength && writeTimeUtc == _logLastWriteTimeUtc)
            {
                return;
            }

            _logLastLength = length;
            _logLastWriteTimeUtc = writeTimeUtc;

            var text = await Task.Run(() => ReadTailLines(_logFilePath, LogTailMaxLines, LogTailReadMaxBytes));
            
            // 截断超长文本
            if (text.Length > LogTextBoxMaxLength)
            {
                text = text.Substring(text.Length - LogTextBoxMaxLength);
                // 找到第一个换行符，确保从完整行开始
                var firstNewLine = text.IndexOf('\n');
                if (firstNewLine > 0)
                {
                    text = text.Substring(firstNewLine + 1);
                }
            }

            // 使用AppendText增量更新，避免替换整个文本导致的闪烁
            if (_logTextBox.Text.Length == 0 || force)
            {
                _logTextBox.Text = text;
            }
            else
            {
                // 只追加新内容
                var currentText = _logTextBox.Text;
                var newContent = text;
                
                // 如果内容变化较大（超过50%），直接替换
                if (newContent.Length > currentText.Length * 1.5 || 
                    !newContent.EndsWith(currentText.Substring(Math.Max(0, currentText.Length - 100))))
                {
                    _logTextBox.Text = newContent;
                }
                else if (newContent.Length > currentText.Length)
                {
                    // 追加新增部分
                    var appendText = newContent.Substring(currentText.Length);
                    _logTextBox.AppendText(appendText);
                }
                else if (newContent != currentText)
                {
                    _logTextBox.Text = newContent;
                }
            }

            // 保持滚动到底部
            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.SelectionLength = 0;
            _logTextBox.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _logTextBox.Text = ex.Message;
        }
        finally
        {
            _isLogReading = false;
        }
    }

    private static string ReadTailLines(string path, int maxLines, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var start = Math.Max(0, length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);

        var readLength = (int)Math.Min(maxBytes, length - start);
        var buffer = new byte[readLength];
        var totalRead = 0;
        while (totalRead < readLength)
        {
            var read = stream.Read(buffer, totalRead, readLength - totalRead);
            if (read <= 0)
            {
                break;
            }
            totalRead += read;
        }

        if (totalRead <= 0)
        {
            return "";
        }

        var text = DecodeText(buffer.AsSpan(0, totalRead).ToArray());
        if (start > 0)
        {
            var cut = text.IndexOf('\n');
            if (cut >= 0 && cut + 1 < text.Length)
            {
                text = text[(cut + 1)..];
            }
        }

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n', StringSplitOptions.None);
        var take = Math.Min(maxLines, lines.Length);
        var startLine = Math.Max(0, lines.Length - take);
        return string.Join(Environment.NewLine, lines, startLine, lines.Length - startLine);
    }

    private static string DecodeText(byte[] bytes)
    {
        string utf8Text;
        using (var ms = new MemoryStream(bytes))
        using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            utf8Text = reader.ReadToEnd();
        }

        var replacementCount = 0;
        foreach (var ch in utf8Text)
        {
            if (ch == '\uFFFD')
            {
                replacementCount++;
            }
        }

        if (replacementCount <= 0)
        {
            return utf8Text;
        }

        var ratio = (double)replacementCount / Math.Max(1, utf8Text.Length);
        if (ratio < 0.01)
        {
            return utf8Text;
        }

        using var ms2 = new MemoryStream(bytes);
        using var reader2 = new StreamReader(ms2, Encoding.Default, detectEncodingFromByteOrderMarks: true);
        return reader2.ReadToEnd();
    }

    private async Task StartServiceAsync()
    {
        await RunOperationAsync("启动中…", async ct =>
        {
            await _serviceManager.StartAsync(DefaultTimeout, ct);
        });
    }

    private async Task StopServiceAsync()
    {
        await RunOperationAsync("停止中…", async ct =>
        {
            await _serviceManager.StopAsync(DefaultTimeout, ct);
        });
    }

    private async Task RestartServiceAsync()
    {
        var result = MessageBox.Show(
            $"确认要重启：{_serviceDisplayName}（{_serviceManager.ServiceName}）？",
            "确认重启",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (result != DialogResult.OK)
        {
            return;
        }

        await RunOperationAsync("重启中…", async ct =>
        {
            await _serviceManager.RestartAsync(DefaultTimeout, ct);
        });
    }

    private async Task RunOperationAsync(string workingText, Func<CancellationToken, Task> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            SetMessage(workingText, isError: false);
            SetButtonsEnabled(false, false, false);

            using var cts = new CancellationTokenSource(DefaultTimeout + TimeSpan.FromSeconds(5));
            await action(cts.Token);

            SetMessage("完成。", isError: false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, isError: true);
        }
        finally
        {
            _isBusy = false;
            RefreshUi();
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
