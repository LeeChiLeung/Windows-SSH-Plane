using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowsSshPlane.UI;

/// <summary>调整空闲超时：连续多久没有数据传输就自动断开隧道。</summary>
public sealed class IdleTimeoutForm : Form
{
    private readonly NumericUpDown _numMinutes = new();

    public IdleTimeoutForm(int currentMinutes)
    {
        BuildUi(currentMinutes);
    }

    /// <summary>用户确认后的分钟数；0 表示不启用空闲断开。</summary>
    public int Minutes => (int)_numMinutes.Value;

    private void BuildUi(int currentMinutes)
    {
        Text = "空闲超时设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(460, 236);

        var tip = new Label
        {
            Dock = DockStyle.Top,
            Height = 96,
            Padding = new Padding(14, 12, 14, 4),
            Text = "隧道内连续多少分钟没有数据传输，就自动断开这条 SSH 连接。\r\n"
                 + "判定依据是通过 SSH 通道实际传输的字节数（保活心跳不计入），"
                 + "所以长时间挂着却不传数据的连接也会被断开。\r\n"
                 + "填 0 表示不自动断开，隧道会一直保持到手动断开或网络中断。",
            ForeColor = Color.DimGray,
        };

        var row = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(14, 8, 14, 8) };
        var label = new Label
        {
            Text = "空闲阈值",
            AutoSize = false,
            Width = 76,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _numMinutes.Minimum = 0;
        _numMinutes.Maximum = 1440;
        _numMinutes.Value = Math.Clamp(currentMinutes, 0, 1440);
        _numMinutes.Width = 110;
        _numMinutes.Height = 30;
        var unit = new Label
        {
            Text = "分钟（0 = 不自动断开）",
            AutoSize = false,
            Width = 200,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };

        label.Location = new Point(14, 10);
        _numMinutes.Location = new Point(96, 8);
        unit.Location = new Point(214, 10);
        row.Controls.Add(label);
        row.Controls.Add(_numMinutes);
        row.Controls.Add(unit);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(0, 10, 14, 10),
            WrapContents = false,
        };
        var btnCancel = new Button { Text = "取消", Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "保存", Width = 92, Height = 30, DialogResult = DialogResult.OK };
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnOk);

        Controls.Add(buttons);
        Controls.Add(row);
        Controls.Add(tip);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}