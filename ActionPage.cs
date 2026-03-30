using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RestartWindowsService;

internal sealed class ActionPage : Panel
{
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    private const string ApiBaseUrl = "http://127.0.0.1:5000";

    private Button _weldCompleteButton = null!;
    private Button _smallScanButton = null!;
    private Button _largeScanButton = null!;
    private LoadingOverlay _loadingOverlay = null!;

    public ActionPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(16);

        // 主布局
        var layout = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
        };

        // 按钮区域 - 左上角
        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            AutoSize = true,
            Location = new Point(0, 0),
        };

        // 焊接完成按钮 - 信号地址 33
        _weldCompleteButton = new Button
        {
            Text = "焊接完成",
            Size = new Size(120, 40),
            Margin = new Padding(0, 0, 10, 0),
        };
        ApplyButtonStyle(_weldCompleteButton);
        _weldCompleteButton.Click += async (_, _) => await OnSignalSendClickedAsync(33, "焊接完成");

        // 小线扫完成按钮 - 信号地址 30
        _smallScanButton = new Button
        {
            Text = "小线扫完成",
            Size = new Size(120, 40),
            Margin = new Padding(0, 0, 10, 0),
        };
        ApplyButtonStyle(_smallScanButton);
        _smallScanButton.Click += async (_, _) => await OnSignalSendClickedAsync(30, "小线扫完成");

        // 大线扫完成按钮 - 信号地址 31
        _largeScanButton = new Button
        {
            Text = "大线扫完成",
            Size = new Size(120, 40),
            Margin = new Padding(0, 0, 10, 0),
        };
        ApplyButtonStyle(_largeScanButton);
        _largeScanButton.Click += async (_, _) => await OnSignalSendClickedAsync(31, "大线扫完成");

        buttonPanel.Controls.Add(_weldCompleteButton);
        buttonPanel.Controls.Add(_smallScanButton);
        buttonPanel.Controls.Add(_largeScanButton);

        layout.Controls.Add(buttonPanel);

        // 加载遮罩层 - 半透明悬浮效果
        _loadingOverlay = new LoadingOverlay
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };

        Controls.Add(layout);
        Controls.Add(_loadingOverlay);
    }

    // 旋转加载动画控件
    private sealed class LoadingOverlay : Panel
    {
        private readonly System.Windows.Forms.Timer _timer;
        private float _rotationAngle = 0;
        private readonly Label _textLabel;

        public LoadingOverlay()
        {
            BackColor = Color.FromArgb(180, 255, 255, 255);
            DoubleBuffered = true;

            _textLabel = new Label
            {
                Text = "处理中...",
                AutoSize = true,
                Font = new Font(Control.DefaultFont.FontFamily, 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 99, 198),
                BackColor = Color.Transparent,
            };
            Controls.Add(_textLabel);

            _timer = new System.Windows.Forms.Timer { Interval = 30 };
            _timer.Tick += (_, _) =>
            {
                _rotationAngle += 15;
                if (_rotationAngle >= 360) _rotationAngle = 0;
                Invalidate();
            };

            Resize += (_, _) =>
            {
                _textLabel.Location = new Point(
                    (Width - _textLabel.Width) / 2,
                    (Height - _textLabel.Height) / 2 + 40);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var centerX = Width / 2;
            var centerY = Height / 2 - 20;
            var radius = 20;

            // 绘制旋转的圆圈
            for (int i = 0; i < 8; i++)
            {
                var angle = (_rotationAngle + i * 45) * (float)Math.PI / 180;
                var x = centerX + (float)Math.Cos(angle) * radius;
                var y = centerY + (float)Math.Sin(angle) * radius;

                var alpha = (int)(255 * (i + 1) / 8.0);
                using var brush = new SolidBrush(Color.FromArgb(alpha, 15, 99, 198));
                g.FillEllipse(brush, x - 4, y - 4, 8, 8);
            }
        }

        public new void Show()
        {
            _timer.Start();
            Visible = true;
            BringToFront();
        }

        public new void Hide()
        {
            _timer.Stop();
            Visible = false;
        }
    }

    private static void ApplyButtonStyle(Button button)
    {
        // 浅蓝色样式 - 淡蓝色背景，蓝色文字，无边框
        var normalBack = Color.FromArgb(196, 215, 242);
        var hoverBack = Color.FromArgb(176, 195, 222);
        var textColor = Color.FromArgb(15, 99, 198);

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = normalBack;
        button.ForeColor = textColor;
        button.Cursor = Cursors.Hand;
        button.Font = new Font(Control.DefaultFont.FontFamily, 10f, FontStyle.Bold);

        button.MouseEnter += (_, _) => button.BackColor = hoverBack;
        button.MouseLeave += (_, _) => button.BackColor = normalBack;
    }

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            _weldCompleteButton.Enabled = false;
            _smallScanButton.Enabled = false;
            _largeScanButton.Enabled = false;
            _loadingOverlay.Show();
        }
        else
        {
            _weldCompleteButton.Enabled = true;
            _smallScanButton.Enabled = true;
            _largeScanButton.Enabled = true;
            _loadingOverlay.Hide();
        }
    }

    private async Task OnSignalSendClickedAsync(int address, string signalName)
    {
        SetLoading(true);

        try
        {
            var requestData = new
            {
                operation = "write",
                address = address,
                value = false
            };

            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await HttpClient.PostAsync($"{ApiBaseUrl}/api/Robot/do-signal", content);
            var responseText = await response.Content.ReadAsStringAsync();

            // 解析返回报文，检查 ret 字段
            if (TryParseRet(responseText, out var ret) && ret == 1)
            {
                System.Windows.Forms.MessageBox.Show($"{signalName}信号发送成功！", "成功", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            }
            else
            {
                System.Windows.Forms.MessageBox.Show($"{signalName}发送失败！\n返回报文：{responseText}", "错误", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show($"{signalName}调用接口失败：{ex.Message}", "错误", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private static bool TryParseRet(string json, out int ret)
    {
        ret = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ret", out var retElement))
            {
                if (retElement.ValueKind == JsonValueKind.Number)
                {
                    ret = retElement.GetInt32();
                }
                else if (retElement.ValueKind == JsonValueKind.String)
                {
                    int.TryParse(retElement.GetString(), out ret);
                }
                return true;
            }
        }
        catch
        {
            // 解析失败
        }
        return false;
    }
}
