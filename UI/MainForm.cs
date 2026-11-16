using System.ComponentModel;
using SshTunnelManager.Core;

namespace SshTunnelManager.UI;

/// <summary>主窗口：管理 SSH 连接与多条反向端口转发规则。</summary>
public sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly BindingList<TunnelRule> _rules;
    private readonly TunnelService _service = new();
    private readonly Label _lblServer = new();
    private readonly Label _lblState = new();
    private readonly Label _lblKeepAlive = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _log = new();
    private readonly Button _btnConnect = new();
    private readonly Button _btnDisconnect = new();
    private readonly Button _btnReconnect = new();
    private readonly Button _btnAccount = new();
    private readonly NotifyIcon _tray = new();
    private bool _busy;
    private bool _closingConfirmed;
    private readonly bool _autoConnect;
    private string _password;

    public MainForm(AppConfig config, string password, bool autoConnect = true)
    {
        _config = config;
        _password = password;
        _autoConnect = autoConnect;
        _rules = new BindingList<TunnelRule>(config.Rules);

        BuildUi();

        _service.UiDispatch = RunOnUi;
        _service.SetCredentials(config, password);
        _service.LogReceived += (_, line) => AppendLog(line);
        _service.StateChanged += (_, _) => RefreshState();

        AppendLog($"程序就绪：{_service.ServerSummary}（保活 {_service.KeepAliveSeconds}s × {_service.KeepAliveCountMax}）");
        RefreshState();
    }

    private void BuildUi()
    {
        Text = "SSH 反向端口转发管理器";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(1020, 720);
        MinimumSize = new Size(880, 600);

        _lblServer.Text = $"服务器：{_config.ServerUser}@{_config.ServerHost}:{_config.ServerPort}";
        _lblServer.AutoSize = true;
        _lblServer.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);

        _lblState.Text = "状态：未连接";
        _lblState.AutoSize = true;
        _lblState.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);

        _lblKeepAlive.Text = $"保活：每 {_config.KeepAliveSeconds} 秒探测，断线最多自动重连 {_config.KeepAliveCountMax} 次";
        _lblKeepAlive.AutoSize = true;
        _lblKeepAlive.ForeColor = Color.DimGray;

        var header = new Panel { Dock = DockStyle.Fill };
        var info = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(2, 4, 0, 0),
        };
        info.Controls.Add(_lblServer);
        info.Controls.Add(_lblState);
        info.Controls.Add(_lblKeepAlive);

        var headerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 18, 4, 0),
        };
        PrepareButton(_btnConnect, "连接", Color.ForestGreen);
        PrepareButton(_btnDisconnect, "断开", Color.DimGray);
        PrepareButton(_btnReconnect, "重新连接", Color.SteelBlue);
        PrepareButton(_btnAccount, "修改账号/密码", Color.DimGray);
        _btnAccount.Width = 136;
        headerButtons.Controls.Add(_btnConnect);
        headerButtons.Controls.Add(_btnDisconnect);
        headerButtons.Controls.Add(_btnReconnect);
        headerButtons.Controls.Add(_btnAccount);

        header.Controls.Add(info);
        header.Controls.Add(headerButtons);

        _btnConnect.Click += async (_, _) => await ConnectAsync();
        _btnReconnect.Click += async (_, _) => await ConnectAsync();
        _btnDisconnect.Click += async (_, _) =>
        {
            await _service.DisconnectAsync();
            AppendLog("已手动断开连接");
        };
        _btnAccount.Click += async (_, _) => await ChangeAccountAsync();

        ConfigureGrid();

        var ruleButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(2, 6, 0, 0),
        };
        var btnAdd = new Button { Text = "＋ 添加转发", Width = 112, Height = 32 };
        var btnEdit = new Button { Text = "编辑", Width = 76, Height = 32 };
        var btnDelete = new Button { Text = "删除", Width = 76, Height = 32 };
        var btnEnable = new Button { Text = "启用", Width = 76, Height = 32 };
        var btnDisable = new Button { Text = "停用", Width = 76, Height = 32 };
        btnAdd.Click += (_, _) => AddRule();
        btnEdit.Click += (_, _) => EditRule();
        btnDelete.Click += (_, _) => DeleteRule();
        btnEnable.Click += (_, _) => SetSelectedEnabled(true);
        btnDisable.Click += (_, _) => SetSelectedEnabled(false);
        var hint = new Label
        {
            Text = "提示：勾选“启用”即按需开关单条转发；双击行可编辑；转发在连接状态下立即生效。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(12, 12, 0, 0),
        };
        ruleButtons.Controls.Add(btnAdd);
        ruleButtons.Controls.Add(btnEdit);
        ruleButtons.Controls.Add(btnDelete);
        ruleButtons.Controls.Add(btnEnable);
        ruleButtons.Controls.Add(btnDisable);
        ruleButtons.Controls.Add(hint);

        var logPanel = new Panel { Dock = DockStyle.Fill };
        var logHeader = new Label
        {
            Text = "运行日志（同时写入 %APPDATA%\\SshTunnelManager\\app.log）",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.DimGray,
        };

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.WordWrap = false;
        _log.BackColor = Color.White;
        _log.Font = new Font("Consolas", 9f);

        var logMenu = new ContextMenuStrip();
        var miCopy = new ToolStripMenuItem("复制全部");
        miCopy.Click += (_, _) =>
        {
            if (_log.TextLength > 0)
            {
                Clipboard.SetText(_log.Text);
            }
        };
        var miClear = new ToolStripMenuItem("清空日志");
        miClear.Click += (_, _) => _log.Clear();
        logMenu.Items.Add(miCopy);
        logMenu.Items.Add(miClear);
        _log.ContextMenuStrip = logMenu;

        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(logHeader);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12, 10, 12, 10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(ruleButtons, 0, 2);
        root.Controls.Add(logPanel, 0, 3);

        Controls.Add(root);

        _tray.Icon = SystemIcons.Application;
        _tray.Text = "SSH 反向端口转发";
        _tray.Visible = true;
        var trayMenu = new ContextMenuStrip();
        var miShow = new ToolStripMenuItem("显示主界面");
        miShow.Click += (_, _) => RestoreFromTray();
        var miReconnect = new ToolStripMenuItem("重新连接");
        miReconnect.Click += async (_, _) =>
        {
            RestoreFromTray();
            await ConnectAsync();
        };
        var miDisconnect = new ToolStripMenuItem("断开连接");
        miDisconnect.Click += async (_, _) => await _service.DisconnectAsync();
        var miExit = new ToolStripMenuItem("退出");
        miExit.Click += (_, _) =>
        {
            _closingConfirmed = true;
            Close();
        };
        trayMenu.Items.Add(miShow);
        trayMenu.Items.Add(miReconnect);
        trayMenu.Items.Add(miDisconnect);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(miExit);
        _tray.ContextMenuStrip = trayMenu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private static void PrepareButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.Width = 104;
        button.Height = 34;
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.System;
        button.ForeColor = color;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.RowTemplate.Height = 28;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 32;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "colEnabled",
            HeaderText = "启用",
            DataPropertyName = nameof(TunnelRule.Enabled),
            FillWeight = 8,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colBindHost",
            HeaderText = "服务器绑定地址",
            DataPropertyName = nameof(TunnelRule.BindHost),
            FillWeight = 20,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colBindPort",
            HeaderText = "服务器端口",
            DataPropertyName = nameof(TunnelRule.BindPort),
            FillWeight = 13,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colTargetHost",
            HeaderText = "目标地址（本机）",
            DataPropertyName = nameof(TunnelRule.TargetHost),
            FillWeight = 20,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colTargetPort",
            HeaderText = "目标端口（本机）",
            DataPropertyName = nameof(TunnelRule.TargetPort),
            FillWeight = 13,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colStatus",
            HeaderText = "状态",
            DataPropertyName = nameof(TunnelRule.Status),
            FillWeight = 12,
            ReadOnly = true,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colError",
            HeaderText = "说明",
            DataPropertyName = nameof(TunnelRule.LastError),
            FillWeight = 24,
            ReadOnly = true,
        });

        _grid.DataSource = _rules;
        _grid.CellValueChanged += Grid_CellValueChanged;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                EditRule();
            }
        };
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            AppendLog("表格输入无效（端口需为 1-65535 的整数），请重试");
        };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_autoConnect)
        {
            await ConnectAsync();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _tray.Visible)
        {
            Hide();
            _tray.ShowBalloonTip(1500, "SSH 反向端口转发", "已最小化到托盘，端口转发仍在后台运行。", ToolTipIcon.Info);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closingConfirmed && _service.IsConnected)
        {
            var result = MessageBox.Show(
                this,
                "退出程序将断开 SSH 连接与全部端口转发，确定退出？",
                "退出确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _closingConfirmed = true;
        }

        if (ConfigStore.Save(_config))
        {
            AppendLog("配置已保存：" + ConfigStore.DefaultPath);
        }

        _tray.Visible = false;
        _tray.Dispose();
        _service.Dispose();
        base.OnFormClosing(e);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task ConnectAsync()
    {
        if (_busy)
        {
            return;
        }

        // 以 --no-autoconnect 启动时，第一次连接前才索要密码
        if (string.IsNullOrEmpty(_password))
        {
            using var login = new PasswordForm(_config);
            if (login.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _password = login.Password;
            _lblServer.Text = $"服务器：{_config.ServerUser}@{_config.ServerHost}:{_config.ServerPort}";
            _service.SetCredentials(_config, _password);
            ConfigStore.Save(_config);
        }

        _busy = true;
        RefreshState();
        try
        {
            var error = await _service.ConnectAsync(_rules.ToList());
            if (string.IsNullOrEmpty(error))
            {
                AppendLog("连接已建立，启用中的转发规则均已启动");
            }
            else
            {
                AppendLog("连接未建立：" + error);
                if (IsHandleCreated && !IsDisposed)
                {
                    MessageBox.Show(this, error, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("连接过程异常：" + ex.Message);
        }
        finally
        {
            _busy = false;
            RefreshState();
        }
    }

    private async Task ChangeAccountAsync()
    {
        using var dialog = new PasswordForm(_config);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _lblServer.Text = $"服务器：{_config.ServerUser}@{_config.ServerHost}:{_config.ServerPort}";
        _service.SetCredentials(_config, dialog.Password);
        ConfigStore.Save(_config);
        AppendLog($"已更新账号信息：{_service.ServerSummary}");
        await _service.DisconnectAsync();
        await ConnectAsync();
    }

    private void AddRule()
    {
        var rule = new TunnelRule
        {
            Enabled = true,
            BindHost = "127.0.0.1",
            BindPort = NextFreeBindPort(),
            TargetHost = "127.0.0.1",
            TargetPort = 4000,
            Status = "未启动",
        };

        using var dialog = new RuleEditForm("添加端口转发", rule, _rules.ToList());
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _rules.Add(rule);
        ConfigStore.Save(_config);
        AppendLog($"已添加转发规则：{rule.ForwardArgument}");
        if (_service.IsConnected && rule.Enabled)
        {
            _ = StartRuleAsync(rule);
        }
    }

    private void EditRule()
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            AppendLog("请先在表格中选中一条规则");
            return;
        }

        using var dialog = new RuleEditForm("编辑端口转发", rule, _rules.Where(r => !ReferenceEquals(r, rule)).ToList());
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ConfigStore.Save(_config);
        AppendLog($"已修改转发规则：{rule.ForwardArgument}");
        if (_service.IsConnected)
        {
            _ = RestartRuleAsync(rule);
        }
    }

    private async void DeleteRule()
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            AppendLog("请先在表格中选中一条规则");
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"确定删除该条转发规则？\r\n\r\n{rule.ForwardArgument}",
            "删除确认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await _service.StopRuleAsync(rule);
        _rules.Remove(rule);
        ConfigStore.Save(_config);
        AppendLog($"已删除转发规则：{rule.ForwardArgument}");
    }

    private async void SetSelectedEnabled(bool enabled)
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            AppendLog("请先在表格中选中一条规则");
            return;
        }

        if (rule.Enabled == enabled)
        {
            return;
        }

        rule.Enabled = enabled;
        ConfigStore.Save(_config);
        if (!_service.IsConnected)
        {
            rule.Status = enabled ? "未连接" : "已停用";
            return;
        }

        if (enabled)
        {
            await StartRuleAsync(rule);
        }
        else
        {
            await _service.StopRuleAsync(rule);
            AppendLog($"已停用转发：{rule.ForwardArgument}");
        }
    }

    private async Task StartRuleAsync(TunnelRule rule)
    {
        var error = await _service.StartRuleAsync(rule);
        if (!string.IsNullOrEmpty(error))
        {
            AppendLog($"启动转发失败（{rule.Summary}）：{error}");
        }
        else
        {
            AppendLog($"已启用转发：{rule.ForwardArgument}");
        }
    }

    private async Task RestartRuleAsync(TunnelRule rule)
    {
        await _service.StopRuleAsync(rule);
        if (rule.Enabled)
        {
            await StartRuleAsync(rule);
        }
    }

    private async void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= _rules.Count)
        {
            return;
        }

        var rule = _rules[e.RowIndex];
        var columnName = _grid.Columns[e.ColumnIndex].Name;

        if (columnName == "colEnabled")
        {
            if (!_service.IsConnected)
            {
                rule.Status = rule.Enabled ? "未连接" : "已停用";
                return;
            }

            if (rule.Enabled)
            {
                await StartRuleAsync(rule);
            }
            else
            {
                await _service.StopRuleAsync(rule);
                AppendLog($"已停用转发：{rule.ForwardArgument}");
            }

            return;
        }

        if (columnName is "colBindHost" or "colBindPort" or "colTargetHost" or "colTargetPort")
        {
            if (!rule.Validate(out _))
            {
                AppendLog($"规则参数无效（{rule.Summary}），端口需在 1-65535 之间");
                return;
            }

            if (_service.IsConnected && rule.Enabled)
            {
                await RestartRuleAsync(rule);
            }
        }
    }

    private TunnelRule? SelectedRule()
    {
        return _grid.CurrentRow?.DataBoundItem as TunnelRule;
    }

    private int NextFreeBindPort()
    {
        var used = _rules.Select(r => r.BindPort).ToHashSet();
        var port = 14001;
        while (used.Contains(port) && port < 65535)
        {
            port++;
        }

        return port;
    }

    private void RefreshState()
    {
        if (IsDisposed)
        {
            return;
        }

        var connected = _service.IsConnected;
        _lblState.Text = _busy ? "状态：连接中…" : connected ? "状态：已连接" : "状态：未连接";
        _lblState.ForeColor = connected ? Color.ForestGreen : _busy ? Color.DarkOrange : Color.Firebrick;
        _btnConnect.Enabled = !connected && !_busy;
        _btnDisconnect.Enabled = connected;
        _btnReconnect.Enabled = !_busy;
        _btnAccount.Enabled = !_busy;

        try
        {
            var enabledCount = _rules.Count(r => r.Enabled);
            _tray.Text = connected
                ? $"SSH 反向转发：已连接（启用 {enabledCount} 条）"
                : "SSH 反向转发：未连接";
        }
        catch
        {
            // NotifyIcon 文本长度等异常可忽略
        }
    }

    private void AppendLog(string line)
    {
        if (IsDisposed)
        {
            return;
        }

        if (_log.InvokeRequired)
        {
            try
            {
                _log.BeginInvoke(new Action<string>(AppendLog), line);
            }
            catch
            {
                // 窗口销毁期间忽略
            }

            return;
        }

        if (_log.TextLength > 400_000)
        {
            _log.Text = _log.Text[^200_000..];
        }

        _log.AppendText(line + Environment.NewLine);

        try
        {
            Directory.CreateDirectory(ConfigStore.DataDirectory);
            File.AppendAllText(ConfigStore.LogPath, line + Environment.NewLine);
        }
        catch
        {
            // 日志文件写入失败不影响运行
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch
            {
                // 窗口销毁期间忽略
            }
        }
        else
        {
            action();
        }
    }
}