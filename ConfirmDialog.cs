using System.Drawing;

namespace RestartWindowsService;

internal sealed class ConfirmDialog : Form
{
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public ConfirmDialog(string serviceName, bool isAdministrator)
    {
        Text = "重启 Windows 服务";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.White;
        ClientSize = new Size(460, 230);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.White,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var header = BuildHeader();
        var body = BuildBody(serviceName, isAdministrator);
        var footer = BuildFooter(out _okButton, out _cancelButton);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        _okButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    private static Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 99, 198),
            Padding = new Padding(14, 12, 14, 12),
        };

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "确认重启 Windows 服务",
            ForeColor = Color.White,
            Font = new Font(Control.DefaultFont.FontFamily, 12f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        panel.Controls.Add(title);
        return panel;
    }

    private static Control BuildBody(string serviceName, bool isAdministrator)
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Padding = new Padding(0, 14, 0, 0),
        };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var serviceLabel = new Label
        {
            AutoSize = true,
            Text = "服务名称",
            ForeColor = Color.FromArgb(64, 64, 64),
        };

        var serviceValue = new Label
        {
            AutoSize = true,
            Text = serviceName,
            Font = new Font(Control.DefaultFont.FontFamily, 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 99, 198),
            Padding = new Padding(0, 2, 0, 6),
            AutoEllipsis = true,
            MaximumSize = new Size(400, 0),
        };

        var hint = new Label
        {
            AutoSize = true,
            Text = "按 Enter 确认重启；按 Esc 或点击“取消”退出。",
            ForeColor = Color.FromArgb(90, 90, 90),
        };

        var adminHintText = isAdministrator
            ? "当前已是管理员权限。"
            : "当前不是管理员权限，可能会重启失败（建议右键以管理员身份运行）。";

        var adminHint = new Label
        {
            AutoSize = true,
            Text = adminHintText,
            ForeColor = isAdministrator ? Color.FromArgb(46, 125, 50) : Color.FromArgb(181, 71, 0),
            Padding = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(400, 0),
        };

        body.Controls.Add(serviceLabel, 0, 0);
        body.Controls.Add(serviceValue, 0, 1);
        body.Controls.Add(hint, 0, 2);
        body.Controls.Add(adminHint, 0, 3);

        return body;
    }

    private static Panel BuildFooter(out Button okButton, out Button cancelButton)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
        };

        var cancel = new Button
        {
            Name = "cancelButton",
            Text = "取消",
            Size = new Size(92, 30),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
        };

        var ok = new Button
        {
            Name = "okButton",
            Text = "重启",
            Size = new Size(92, 30),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(15, 99, 198),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        ok.FlatAppearance.BorderSize = 0;

        panel.Controls.Add(cancel);
        panel.Controls.Add(ok);

        panel.Resize += (_, _) =>
        {
            var bottom = panel.ClientSize.Height - 10;
            cancel.Location = new Point(panel.ClientSize.Width - cancel.Width - 10, bottom - cancel.Height);
            ok.Location = new Point(cancel.Left - ok.Width - 10, bottom - ok.Height);
        };

        okButton = ok;
        cancelButton = cancel;

        return panel;
    }
}
