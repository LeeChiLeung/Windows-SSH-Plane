using WindowsSshPlane.Core;

namespace WindowsSshPlane.UI;

/// <summary>新增 / 编辑一条 -R 反向转发规则。</summary>
public sealed class RuleEditForm : Form
{
    private readonly TunnelRule _rule;
    private readonly IReadOnlyList<TunnelRule> _others;
    private readonly TextBox _txtBindHost = new();
    private readonly NumericUpDown _numBindPort = new();
    private readonly TextBox _txtTargetHost = new();
    private readonly NumericUpDown _numTargetPort = new();
    private readonly CheckBox _chkEnabled = new();
    private readonly Label _lblPreview = new();
    private readonly Label _lblError = new();

    public RuleEditForm(string title, TunnelRule rule, IReadOnlyList<TunnelRule> others)
    {
        _rule = rule;
        _others = others;
        BuildUi(title);
    }

    private void BuildUi(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(470, 300);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16, 14, 16, 6),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _txtBindHost.Text = _rule.BindHost;
        _txtBindHost.Dock = DockStyle.Fill;
        _txtBindHost.Margin = new Padding(0, 4, 0, 4);

        _numBindPort.Minimum = 1;
        _numBindPort.Maximum = 65535;
        _numBindPort.Value = Math.Clamp(_rule.BindPort, 1, 65535);
        _numBindPort.Dock = DockStyle.Fill;
        _numBindPort.Margin = new Padding(0, 4, 0, 4);

        _txtTargetHost.Text = _rule.TargetHost;
        _txtTargetHost.Dock = DockStyle.Fill;
        _txtTargetHost.Margin = new Padding(0, 4, 0, 4);

        _numTargetPort.Minimum = 1;
        _numTargetPort.Maximum = 65535;
        _numTargetPort.Value = Math.Clamp(_rule.TargetPort, 1, 65535);
        _numTargetPort.Dock = DockStyle.Fill;
        _numTargetPort.Margin = new Padding(0, 4, 0, 4);

        _chkEnabled.Text = "启用该转发（连接后自动启动）";
        _chkEnabled.Checked = _rule.Enabled;
        _chkEnabled.AutoSize = true;
        _chkEnabled.Margin = new Padding(0, 6, 0, 0);

        _lblPreview.ForeColor = Color.SteelBlue;
        _lblPreview.AutoSize = false;
        _lblPreview.Dock = DockStyle.Fill;

        _lblError.ForeColor = Color.Firebrick;
        _lblError.AutoSize = false;
        _lblError.Dock = DockStyle.Fill;

        layout.Controls.Add(MakeLabel("服务器侧绑定地址"), 0, 0);
        layout.Controls.Add(_txtBindHost, 1, 0);
        layout.Controls.Add(MakeLabel("服务器侧端口"), 0, 1);
        layout.Controls.Add(_numBindPort, 1, 1);
        layout.Controls.Add(MakeLabel("目标地址"), 0, 2);
        layout.Controls.Add(_txtTargetHost, 1, 2);
        layout.Controls.Add(MakeLabel("目标端口"), 0, 3);
        layout.Controls.Add(_numTargetPort, 1, 3);
        layout.Controls.Add(new Label { Text = string.Empty }, 0, 4);
        layout.Controls.Add(_chkEnabled, 1, 4);
        layout.Controls.Add(MakeLabel("等效参数"), 0, 5);
        layout.Controls.Add(_lblPreview, 1, 5);
        layout.Controls.Add(_lblError, 1, 6);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(0, 8, 12, 8),
            WrapContents = false,
        };
        var btnCancel = new Button { Text = "取消", Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "确定", Width = 92, Height = 30 };
        btnOk.Click += OnOk;
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        _numBindPort.ValueChanged += (_, _) => UpdatePreview();
        _numTargetPort.ValueChanged += (_, _) => UpdatePreview();
        _txtBindHost.TextChanged += (_, _) => UpdatePreview();
        _txtTargetHost.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
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

    private void UpdatePreview()
    {
        _lblPreview.Text = $"-R {_txtBindHost.Text.Trim()}:{(int)_numBindPort.Value}:{_txtTargetHost.Text.Trim()}:{(int)_numTargetPort.Value}";
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var bindHost = _txtBindHost.Text.Trim();
        var targetHost = _txtTargetHost.Text.Trim();
        if (bindHost.Length == 0)
        {
            _lblError.Text = "请填写服务器侧绑定地址（一般用 127.0.0.1）";
            _txtBindHost.Focus();
            return;
        }

        if (targetHost.Length == 0)
        {
            _lblError.Text = "请填写目标地址（一般用 127.0.0.1）";
            _txtTargetHost.Focus();
            return;
        }

        var bindPort = (int)_numBindPort.Value;
        var duplicate = _others.FirstOrDefault(o =>
            o.BindHost.Trim().Equals(bindHost, StringComparison.OrdinalIgnoreCase) && o.BindPort == bindPort);
        if (duplicate is not null)
        {
            _lblError.Text = $"服务器侧 {bindHost}:{bindPort} 已被另一条规则占用，请换一个端口";
            _numBindPort.Focus();
            return;
        }

        _rule.Enabled = _chkEnabled.Checked;
        _rule.BindHost = bindHost;
        _rule.BindPort = bindPort;
        _rule.TargetHost = targetHost;
        _rule.TargetPort = (int)_numTargetPort.Value;
        DialogResult = DialogResult.OK;
        Close();
    }
}