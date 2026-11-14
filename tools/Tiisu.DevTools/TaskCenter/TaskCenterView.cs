using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tiisu.DevTools.TaskCenter;

/// <summary>任务中心：分类 + 任务卡片 + 最近产物预览；第一轮发布、第二轮出图。</summary>
internal sealed class TaskCenterView : UserControl
{
    private readonly IReadOnlyList<DevTask> _tasks;
    private readonly Action<string> _outputChanged;
    private readonly Action<string> _log;
    private readonly Action<string, string> _setStatus;
    private readonly Func<DevTask, Task> _execute;
    private readonly ListBox _categories;
    private readonly FlowLayoutPanel _cards;
    private readonly Panel _header;
    private readonly TextBox _outputDir;
    private readonly PictureBox _preview;
    private readonly Label _previewPath;

    public TaskCenterView(
        IReadOnlyList<DevTask> tasks,
        string outputDir,
        Action<string> outputChanged,
        Action<string> log,
        Action<string, string> setStatus,
        Func<DevTask, Task> execute)
    {
        _tasks = tasks;
        _outputChanged = outputChanged;
        _log = log;
        _setStatus = setStatus;
        _execute = execute;

        Dock = DockStyle.Fill;
        Padding = new Padding(6);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        split.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        var title = new Label
        {
            Text = "任务中心",
            Location = new Point(4, 0),
            Size = new Size(300, 30),
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var hint = new Label
        {
            Text = "按分类选任务，产物与说明都在卡片上。",
            Location = new Point(340, 4),
            Size = new Size(420, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.DimGray,
        };
        var outLabel = new Label
        {
            Text = "出图目录",
            Location = new Point(4, 42),
            Size = new Size(68, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _outputDir = new TextBox
        {
            Text = outputDir,
            Location = new Point(76, 40),
            Size = new Size(520, 24),
        };
        var browse = new Button
        {
            Text = "浏览…",
            Location = new Point(604, 39),
            Size = new Size(76, 26),
            UseVisualStyleBackColor = true,
        };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = Directory.Exists(_outputDir.Text) ? _outputDir.Text : AppContext.BaseDirectory,
                Description = "选择出图输出目录",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _outputDir.Text = dialog.SelectedPath;
            }
        };
        _outputDir.TextChanged += (_, _) => _outputChanged(_outputDir.Text.Trim());
        _header.Controls.Add(title);
        _header.Controls.Add(hint);
        _header.Controls.Add(outLabel);
        _header.Controls.Add(_outputDir);
        _header.Controls.Add(browse);
        _header.Resize += (_, _) =>
        {
            hint.Left = Math.Max(340, _header.Width - 440);
        };

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
        _cards.ClientSizeChanged += (_, _) => ResizeCards();

        var previewBox = new GroupBox
        {
            Text = "最近产物",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 8),
            Margin = new Padding(6, 0, 0, 0),
        };
        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(24, 24, 24),
        };
        _previewPath = new Label
        {
            Dock = DockStyle.Fill,
            Text = "还没有出图。运行一个出图任务后，这里显示缩略图。",
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        previewLayout.Controls.Add(_preview, 0, 0);
        previewLayout.Controls.Add(_previewPath, 0, 1);
        previewBox.Controls.Add(previewLayout);

        split.Controls.Add(_header, 0, 0);
        split.SetColumnSpan(_header, 3);
        split.Controls.Add(_categories, 0, 1);
        split.Controls.Add(_cards, 1, 1);
        split.Controls.Add(previewBox, 2, 1);
        Controls.Add(split);

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
                Text = "这个分类还没迁移；先看「发布」或「出图」。",
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
        int width = Math.Max(380, _cards.ClientSize.Width - 28);
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
            Width = Math.Max(380, _cards.ClientSize.Width - 28),
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
                ShowPreview(task.OutputPath?.Invoke());
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

    private void ShowPreview(string? path)
    {
        _preview.Image?.Dispose();
        _preview.Image = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            _previewPath.Text = "该任务没有图片产物。";
            return;
        }

        _previewPath.Text = path;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
        {
            return;
        }

        try
        {
            using var image = Image.FromFile(path);
            _preview.Image = new Bitmap(image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _previewPath.Text = "预览失败：" + ex.Message;
        }
    }
}
