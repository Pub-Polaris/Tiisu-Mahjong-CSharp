using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Tiisu.DevTools.TaskCenter;

/// <summary>批量审局参数弹窗：局数 / 模式 / jobs / 起始种子 / UI 审计 / 报听分支。</summary>
internal sealed class AuditGamesDialog : Form
{
    private readonly NumericUpDown _count;
    private readonly NumericUpDown _mode;
    private readonly NumericUpDown _jobs;
    private readonly NumericUpDown _seed;
    private readonly CheckBox _ui;
    private readonly CheckBox _passive;
    private readonly CheckBox _call;

    public AuditGamesDialog()
    {
        Text = "批量审局";
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(480, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _count = Number(1, 100_000, 100);
        _mode = Number(1, 16, 4);
        _jobs = Number(1, Math.Max(1, Environment.ProcessorCount), Math.Min(4, Math.Max(1, Environment.ProcessorCount)));
        _seed = Number(0, int.MaxValue, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "局数", _count);
        AddRow(layout, 1, "模式局数", _mode);
        AddRow(layout, 2, "并行 jobs", _jobs);
        AddRow(layout, 3, "起始种子", _seed);

        _ui = new CheckBox { Text = "UI 不变量审计（--ui）", Checked = true, Dock = DockStyle.Fill };
        _passive = new CheckBox { Text = "被动人类：能和也不和（--passive）", Dock = DockStyle.Fill };
        _call = new CheckBox { Text = "覆盖报听分支（--call）", Dock = DockStyle.Fill };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.Controls.Add(_ui, 0, 4);
        layout.SetColumnSpan(_ui, 2);
        layout.Controls.Add(_passive, 0, 5);
        layout.SetColumnSpan(_passive, 2);
        layout.Controls.Add(_call, 0, 6);
        layout.SetColumnSpan(_call, 2);
        Controls.Add(layout);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(12, 8, 12, 8),
        };
        var start = new Button { Text = "开始审计", DialogResult = DialogResult.OK, Width = 100 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
        AcceptButton = start;
        CancelButton = cancel;
    }

    public IReadOnlyList<string> Arguments()
    {
        var args = new List<string>
        {
            "--audit-games",
            ((int)_count.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)_mode.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)_seed.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--jobs",
            ((int)_jobs.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (_ui.Checked) args.Add("--ui");
        if (_passive.Checked) args.Add("--passive");
        if (_call.Checked) args.Add("--call");
        return args;
    }

    private static NumericUpDown Number(int min, int max, int value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Dock = DockStyle.Fill,
    };

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
