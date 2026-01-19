using System.Drawing;
using System.Security.Principal;
using System.ServiceProcess;

namespace RestartWindowsService;

internal sealed class MainForm : Form
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ServiceManager _serviceManager;
    private readonly string _serviceDisplayName;
    private readonly bool _autoStart;

    private readonly Label _statusValue;
    private readonly Label _adminValue;
    private readonly Label _messageValue;

    private readonly Button _startButton;
    private readonly Button _restartButton;
    private readonly Button _stopButton;

    private readonly System.Windows.Forms.Timer _refreshTimer;

    private bool _isBusy;
    private bool _isExiting;
    private NotifyIcon? _trayIcon;

    public MainForm(string serviceName, string serviceDisplayName, bool autoStart)
    {
        _serviceManager = new ServiceManager(serviceName);
        _serviceDisplayName = serviceDisplayName;
        _autoStart = autoStart;

        Text = $"{_serviceDisplayName} 控制";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 420);
        MinimumSize = new Size(760, 420);
        BackColor = Color.White;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.White,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = BuildHeader(_serviceDisplayName);
        var body = BuildBody(out _statusValue, out _adminValue, out _messageValue);
        var footer = BuildFooter(out _startButton, out _restartButton, out _stopButton);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);

        _startButton.Click += async (_, _) => await StartServiceAsync();
        _restartButton.Click += async (_, _) => await RestartServiceAsync();
        _stopButton.Click += async (_, _) => await StopServiceAsync();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _refreshTimer.Tick += (_, _) => RefreshUi();

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

    private static Control BuildHeader(string serviceDisplayName)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(15, 99, 198),
            Padding = new Padding(14, 12, 14, 12),
        };

        var title = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = $"{serviceDisplayName} 控制面板",
            ForeColor = Color.White,
            Font = new Font(Control.DefaultFont.FontFamily, 12f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildBody(out Label statusValue, out Label adminValue, out Label messageValue)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(0, 14, 0, 0),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var infoGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = true,
            BackColor = Color.White,
        };
        infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        //AddRow(infoGrid, 0, "服务名称", new Label
        //{
        //    AutoSize = true,
        //    Text = _serviceDisplayName,
        //    Font = new Font(Control.DefaultFont.FontFamily, 16f, FontStyle.Bold),
        //    ForeColor = Color.FromArgb(15, 99, 198),
        //    MaximumSize = new Size(360, 0),
        //    AutoEllipsis = true,
        //});

        AddRow(infoGrid, 1, "服务名", new Label
        {
            AutoSize = true,
            Text = _serviceManager.ServiceName,
            ForeColor = Color.FromArgb(90, 90, 90),
            MaximumSize = new Size(360, 0),
            AutoEllipsis = true,
        });

        statusValue = new Label
        {
            AutoSize = true,
            Text = "—",
            ForeColor = Color.FromArgb(64, 64, 64),
        };
        AddRow(infoGrid, 2, "运行状态", statusValue);

        adminValue = new Label
        {
            AutoSize = true,
            Text = "—",
        };
        AddRow(infoGrid, 3, "管理员", adminValue);

        var hint = new Label
        {
            AutoSize = true,
            Text = "提示：关闭窗口会缩到托盘运行，不会停止服务。",
            ForeColor = Color.FromArgb(90, 90, 90),
            Padding = new Padding(0, 10, 0, 0),
            MaximumSize = new Size(420, 0),
        };
        infoGrid.Controls.Add(hint, 1, 4);

        messageValue = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            ForeColor = Color.FromArgb(90, 90, 90),
            Padding = new Padding(0, 10, 0, 0),
        };

        outer.Controls.Add(infoGrid, 0, 0);
        outer.Controls.Add(messageValue, 0, 1);

        return outer;
    }

    private static void AddRow(TableLayoutPanel grid, int rowIndex, string labelText, Control valueControl)
    {
        if (grid.RowStyles.Count <= rowIndex)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            ForeColor = Color.FromArgb(64, 64, 64),
            Padding = new Padding(0, 5, 0, 5),
        };

        valueControl.Padding = new Padding(0, 5, 0, 5);

        grid.Controls.Add(label, 0, rowIndex);
        grid.Controls.Add(valueControl, 1, rowIndex);
    }

    private static Control BuildFooter(out Button startButton, out Button restartButton, out Button stopButton)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 12, 0, 0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var stop = new Button
        {
            Text = "停止",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(110, 42),
            Padding = new Padding(14, 8, 14, 8),
        };

        var restart = new Button
        {
            Text = "重启",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(110, 42),
            Padding = new Padding(14, 8, 14, 8),
        };

        var start = new Button
        {
            Text = "启动",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(110, 42),
            Padding = new Padding(14, 8, 14, 8),
            BackColor = Color.FromArgb(15, 99, 198),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        start.FlatAppearance.BorderSize = 0;

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
        buttons.Controls.Add(stop);
        buttons.Controls.Add(restart);
        buttons.Controls.Add(start);
        buttons.SetFlowBreak(start, false);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 0);
        panel.Controls.Add(buttons, 1, 0);

        stopButton = stop;
        restartButton = restart;
        startButton = start;

        return panel;
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
            _statusValue.ForeColor = Color.FromArgb(183, 28, 28);
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
            ? Color.FromArgb(46, 125, 50)
            : Color.FromArgb(90, 90, 90);

        var isAdministrator = IsAdministrator();
        _adminValue.Text = isAdministrator ? "是" : "否";
        _adminValue.ForeColor = isAdministrator ? Color.FromArgb(46, 125, 50) : Color.FromArgb(181, 71, 0);

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
        _messageValue.ForeColor = isError ? Color.FromArgb(183, 28, 28) : Color.FromArgb(90, 90, 90);
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
