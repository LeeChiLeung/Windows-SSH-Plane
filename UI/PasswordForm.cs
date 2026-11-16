using SshTunnelManager.Core;

namespace SshTunnelManager.UI;

/// <summary>启动时弹出的登录框：输入密码（以及可修改的服务器参数）。密码只保留在内存中。</summary>
public sealed class PasswordForm : Form
{
    private readonly AppConfig _config;
    private readonly TextBox _txtHost = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtUser = new();
    private readonly TextBox _txtPassword = new();
    private readonly CheckBox _chkShow = new();
    private readonly Label _lblError = new();

    public PasswordForm(AppConfig config)
    {
        _config = config;
        BuildUi();
    }

    /// <summary>用户输入的密码。</summary>
    public string Password => _txtPassword.Text;

    private void BuildUi()
    {
        Text = "SSH 反向端口转发 - 登录";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(460, 268);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16, 14, 16, 6),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _txtHost.Text = _config.ServerHost;
        _txtHost.Dock = DockStyle.Fill;
        _txtHost.Margin = new Padding(0, 4, 0, 4);

        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = Math.Clamp(_config.ServerPort, 1, 65535);
        _numPort.Dock = DockStyle.Fill;
        _numPort.Margin = new Padding(0, 4, 0, 4);

        _txtUser.Text = _config.ServerUser;
        _txtUser.Dock = DockStyle.Fill;
        _txtUser.Margin = new Padding(0, 4, 0, 4);

        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.Margin = new Padding(0, 4, 0, 4);
        _txtPassword.UseSystemPasswordChar = true;

        _chkShow.Text = "显示密码";
        _chkShow.AutoSize = true;
        _chkShow.Margin = new Padding(0, 6, 0, 0);
        _chkShow.CheckedChanged += (_, _) => _txtPassword.UseSystemPasswordChar = !_chkShow.Checked;

        _lblError.ForeColor = Color.Firebrick;
        _lblError.AutoSize = false;
        _lblError.Dock = DockStyle.Fill;

        layout.Controls.Add(MakeLabel("服务器"), 0, 0);
        layout.Controls.Add(_txtHost, 1, 0);
        layout.Controls.Add(MakeLabel("端口"), 0, 1);
        layout.Controls.Add(_numPort, 1, 1);
        layout.Controls.Add(MakeLabel("用户名"), 0, 2);
        layout.Controls.Add(_txtUser, 1, 2);
        layout.Controls.Add(MakeLabel("密码"), 0, 3);
        layout.Controls.Add(_txtPassword, 1, 3);
        layout.Controls.Add(new Label { Text = string.Empty }, 0, 4);
        layout.Controls.Add(_chkShow, 1, 4);
        layout.Controls.Add(_lblError, 1, 5);

        var tip = new Label
        {
            Text = "密码仅用于本次运行，不会写入配置文件；关闭程序后需重新输入。",
            ForeColor = Color.DimGray,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 92 };
        tip.SetBounds(16, 4, 428, 34);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 6, 12, 6),
            WrapContents = false,
        };

        var btnCancel = new Button { Text = "取消", Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "连接", Width = 92, Height = 30 };
        btnOk.Click += OnOk;
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnOk);
        bottom.Controls.Add(tip);
        bottom.Controls.Add(buttons);

        Controls.Add(layout);
        Controls.Add(bottom);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 6, 4),
        };
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var host = _txtHost.Text.Trim();
        var user = _txtUser.Text.Trim();
        if (host.Length == 0)
        {
            _lblError.Text = "请填写服务器地址";
            _txtHost.Focus();
            return;
        }

        if (user.Length == 0)
        {
            _lblError.Text = "请填写用户名";
            _txtUser.Focus();
            return;
        }

        if (_txtPassword.Text.Length == 0)
        {
            _lblError.Text = "请填写登录密码";
            _txtPassword.Focus();
            return;
        }

        _config.ServerHost = host;
        _config.ServerPort = (int)_numPort.Value;
        _config.ServerUser = user;
        DialogResult = DialogResult.OK;
        Close();
    }
}