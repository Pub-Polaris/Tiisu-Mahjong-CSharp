using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tiisu.DevTools.TaskCenter;

/// <summary>任务中心：左侧分类，右侧任务卡片；第一轮只有发布任务。</summary>
internal sealed class TaskCenterView : UserControl
{
    private readonly IReadOnlyList<DevTask> _tasks;
    private readonly Action<string> _log;
    private readonly Action<string, string> _setStatus;
    private readonly Func<DevTask, Task> _execute;
    private readonly ListBox _categories;
    private readonly FlowLayoutPanel _cards;
    private readonly Panel _header;

    public TaskCenterView(
        IReadOnlyList<DevTask> tasks,
        Action<string> log,
        Action<string, string> setStatus,
        Func<DevTask, Task> execute)
    {
        _tasks = tasks;
        _log = log;
        _setStatus = setStatus;
        _execute = execute;

        Dock = DockStyle.Fill;
        Padding = new Padding(6);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        var title = new Label
        {
            Text = "任务中心",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
        };
        var hint = new Label
        {
            Text = "按分类选任务，产物与说明都在卡片上。",
            Dock = DockStyle.Right,
            Width = 320,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.DimGray,
        };
        _header.Controls.Add(title);
        _header.Controls.Add(hint);

        _categories = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Font = new Font("Microsoft YaHei UI", 10F),
            Margin = new Padding(0, 0, 6, 0),
        };
        foreach (string category in DevTaskRegistry.Categories)
        {
            _categories.Items.Add(category);
        }

        _categories.SelectedIndex = 0;
        _categories.SelectedIndexChanged += (_, _) => Rebuild();

        _cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4),
            Margin = new Padding(0),
            BackColor = Color.FromArgb(248, 248, 248),
        };

        split.Controls.Add(_header, 0, 0);
        split.SetColumnSpan(_header, 2);
        split.Controls.Add(_categories, 0, 1);
        split.Controls.Add(_cards, 1, 1);
        Controls.Add(split);
        _cards.ClientSizeChanged += (_, _) => ResizeCards();

        _categories.SelectedIndex = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        _cards.SuspendLayout();
        _cards.Controls.Clear();

        string category = _categories.SelectedItem as string ?? DevTaskRegistry.PublishCategory;
        var tasks = _tasks.Where(t => t.Category == category).ToArray();
        if (tasks.Length == 0)
        {
            _cards.Controls.Add(new Label
            {
                Text = "这个分类还没迁移，先看「发布」。",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(12),
            });
        }

        foreach (DevTask task in tasks)
        {
            _cards.Controls.Add(BuildCard(task));
        }

        _cards.ResumeLayout(performLayout: true);
    }

    private void ResizeCards()
    {
        int width = Math.Max(420, _cards.ClientSize.Width - 28);
        foreach (Control control in _cards.Controls)
        {
            if (control is not Panel card)
            {
                continue;
            }

            card.Width = width;
            foreach (Control child in card.Controls)
            {
                if (child is Button button)
                {
                    button.Left = card.Width - button.Width - 16;
                }
                else if (child is Label label && label.Tag as string == "wide")
                {
                    label.Width = card.Width - 150;
                }
            }
        }
    }

    private Control BuildCard(DevTask task)
    {
        var card = new Panel
        {
            Width = Math.Max(420, _cards.ClientSize.Width - 28),
            Height = 96,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6, 6, 6, 0),
        };

        var title = new Label
        {
            Text = task.Title,
            Location = new Point(12, 8),
            Size = new Size(300, 24),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        };
        var summary = new Label
        {
            Text = task.Summary,
            Location = new Point(12, 34),
            Size = new Size(card.Width - 150, 22),
            ForeColor = Color.FromArgb(70, 70, 70),
            AutoEllipsis = true,
        };
        var output = new Label
        {
            Text = "产物：" + task.OutputHint,
            Location = new Point(12, 60),
            Size = new Size(card.Width - 150, 20),
            ForeColor = Color.DimGray,
            Font = new Font("Microsoft YaHei UI", 8.5F),
        };
        var state = new Label
        {
            Text = string.Empty,
            Location = new Point(12, 76),
            Size = new Size(card.Width - 150, 16),
            ForeColor = Color.FromArgb(30, 110, 70),
            Font = new Font("Microsoft YaHei UI", 8F),
        };

        var run = new Button
        {
            Text = "运行",
            Size = new Size(84, 30),
            Location = new Point(card.Width - 104, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            UseVisualStyleBackColor = true,
        };
        run.Click += async (_, _) =>
        {
            if (task.Confirm && MessageBox.Show(
                    this,
                    "会覆盖现有产物，继续？\n\n" + task.Title,
                    "确认执行",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _log("  已取消：" + task.Title);
                return;
            }

            run.Enabled = false;
            state.Text = "运行中…";
            state.ForeColor = Color.DarkOrange;
            _setStatus(task.Title + " …", string.Empty);
            try
            {
                await _execute(task);
                state.Text = "完成";
                state.ForeColor = Color.FromArgb(30, 110, 70);
            }
            catch (Exception ex)
            {
                state.Text = "失败：" + ex.Message;
                state.ForeColor = Color.Firebrick;
                _log("  ! " + task.Title + "：" + ex.Message);
            }
            finally
            {
                run.Enabled = true;
            }
        };

        summary.Tag = "wide";
        output.Tag = "wide";
        card.Controls.Add(title);
        card.Controls.Add(summary);
        card.Controls.Add(output);
        card.Controls.Add(state);
        card.Controls.Add(run);
        ResizeCards();
        return card;
    }
}
