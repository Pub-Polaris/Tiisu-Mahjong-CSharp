using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Tiisu.Localization;

namespace Tiisu.DevTools.TaskCenter;

/// <summary>自博弈导出参数弹窗；不拼命令行，直接给局数/jobs/采样/探索率和输出文件。</summary>
internal sealed class SelfPlayDialog : Form
{
    private readonly NumericUpDown _games;
    private readonly NumericUpDown _jobs;
    private readonly NumericUpDown _sampleEvery;
    private readonly NumericUpDown _explore;
    private readonly TextBox _output;

    public SelfPlayDialog(string workingDirectory)
    {
        Text = L.T("tools.devtools.dialog.selfplay.title");
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(520, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _games = Number(1, 200_000, 1000);
        _jobs = Number(1, Math.Max(1, Environment.ProcessorCount), Math.Min(8, Math.Max(1, Environment.ProcessorCount)));
        _sampleEvery = Number(1, 100, 10);
        _explore = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 0.9m,
            DecimalPlaces = 2,
            Increment = 0.05m,
            Value = 0.10m,
            Dock = DockStyle.Fill,
        };

        string directory = Path.Combine(workingDirectory, "artifacts", "selfplay");
        Directory.CreateDirectory(directory);
        _output = new TextBox
        {
            Text = Path.Combine(directory, "selfplay-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl"),
            Dock = DockStyle.Fill,
        };
        var browse = new Button { Text = L.T("tools.devtools.dialog.selfplay.browse"), Width = 76, Dock = DockStyle.Right };
        browse.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = L.T("tools.devtools.dialog.selfplay.saveTitle"),
                Filter = L.T("tools.devtools.dialog.selfplay.filter"),
                InitialDirectory = directory,
                FileName = Path.GetFileName(_output.Text),
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _output.Text = dialog.FileName;
            }
        };
        var outputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        outputRow.Controls.Add(_output, 0, 0);
        outputRow.Controls.Add(browse, 1, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, L.T("tools.devtools.dialog.selfplay.field.games"), _games);
        AddRow(layout, 1, L.T("tools.devtools.dialog.selfplay.field.jobs"), _jobs);
        AddRow(layout, 2, L.T("tools.devtools.dialog.selfplay.field.sampleEvery"), _sampleEvery);
        AddRow(layout, 3, L.T("tools.devtools.dialog.selfplay.field.explore"), _explore);
        AddRow(layout, 4, L.T("tools.devtools.dialog.selfplay.field.output"), outputRow);

        var hint = new Label
        {
            Text = L.T("tools.devtools.dialog.selfplay.hint"),
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(hint, 0, 5);
        layout.SetColumnSpan(hint, 2);
        Controls.Add(layout);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(12, 8, 12, 8),
        };
        var start = new Button { Text = L.T("tools.devtools.dialog.selfplay.start"), DialogResult = DialogResult.OK, Width = 100 };
        var cancel = new Button { Text = L.T("tools.devtools.dialog.selfplay.cancel"), DialogResult = DialogResult.Cancel, Width = 80 };
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
        AcceptButton = start;
        CancelButton = cancel;
    }

    public int Games => (int)_games.Value;
    public int Jobs => (int)_jobs.Value;
    public int SampleEvery => (int)_sampleEvery.Value;
    public double Explore => (double)_explore.Value;
    public string OutputPath => _output.Text.Trim();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(OutputPath))
        {
            MessageBox.Show(this, L.T("tools.devtools.dialog.selfplay.emptyOutput"), L.T("tools.devtools.dialog.selfplay.paramError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        base.OnFormClosing(e);
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
