using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.Localization;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 本地玩家档案页：列表、切换、新建、重命名、删除。名字输入走 IME 文本路由。
/// </summary>
public sealed class ProfileScreen : Screen
{
    private static readonly Color Gold = Color.Rgb(242, 201, 76);
    private static readonly Color Text = Color.Rgb(222, 230, 220);
    private static readonly Color Muted = Color.Rgb(150, 168, 158);
    private static readonly Color Panel = Color.Rgb(18, 30, 34);
    private static readonly Color PanelBorder = Color.Rgb(42, 62, 62);
    private static readonly Color Row = Color.Rgb(24, 38, 40);
    private static readonly Color RowSelected = Color.Rgb(40, 64, 56);
    private static readonly Color Danger = Color.Rgb(196, 108, 108);

    private readonly PlayerProfileStore _store;
    private readonly string _currentId;
    private readonly Action _back;
    private readonly Action<PlayerProfile> _switchProfile;
    private readonly Action<string>? _nameChanged;

    private IReadOnlyList<PlayerProfile> _items = Array.Empty<PlayerProfile>();
    private int _selected;
    private int _scroll;
    private bool _editing;
    private bool _creating;
    private bool _confirmDelete;
    private string _editBuffer = string.Empty;
    private string _editError = string.Empty;
    private string _status = string.Empty;

    private readonly List<(PlayerProfile Profile, RectF Rect)> _rowRects = new();
    private RectF _btnPrimary;
    private RectF _btnNew;
    private RectF _btnRename;
    private RectF _btnDelete;
    private RectF _btnBack;
    private RectF _btnSave;
    private RectF _btnCancel;
    private RectF _btnConfirm;
    private RectF _btnConfirmCancel;

    private bool _hoverPrimary;
    private bool _hoverNew;
    private bool _hoverRename;
    private bool _hoverDelete;
    private bool _hoverBack;
    private bool _hoverSave;
    private bool _hoverCancel;
    private bool _hoverConfirm;
    private bool _hoverConfirmCancel;

    public ProfileScreen(
        PlayerProfileStore store,
        string currentProfileId,
        Action back,
        Action<PlayerProfile> switchProfile,
        Action<string>? nameChanged = null) : base("profiles")
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _currentId = currentProfileId ?? string.Empty;
        _back = back ?? (() => { });
        _switchProfile = switchProfile ?? (_ => { });
        _nameChanged = nameChanged;
        Reload();
    }

    public void Reload()
    {
        _store.Reload();
        _items = _store.Profiles;
        if (_items.Count == 0)
        {
            _selected = 0;
            _scroll = 0;
            return;
        }

        int activeIndex = _items.ToList().FindIndex(p => p.Id == _currentId);
        _selected = activeIndex >= 0 ? activeIndex : Math.Clamp(_selected, 0, _items.Count - 1);
        EnsureVisible();
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0, Color.Rgb(20, 28, 34), Color.Rgb(8, 12, 16));

        c.DrawText(L.T("app.ui.profile.title"), new RectF(0, 38, Width, 50), Gold,
            new TextStyle(30, Bold: true, Center: true));
        c.DrawText(L.T("app.ui.profile.subtitle"), new RectF(0, 86, Width, 24), Muted,
            new TextStyle(14, Center: true));

        DrawList(c);
        DrawActions(c);
        if (_status.Length > 0)
        {
            c.DrawText(_status, new RectF(0, 505, Width, 22), Muted, new TextStyle(13, Center: true));
        }

        if (_editing)
        {
            DrawEdit(c);
        }
        else if (_confirmDelete)
        {
            DrawConfirm(c);
        }
    }

    private void DrawList(ICanvas c)
    {
        var list = new RectF(120, 124, 1040, 356);
        c.FillRoundRect(list, 16, Panel);
        c.StrokeRoundRect(list, 16, PanelBorder, 1.4f);

        _rowRects.Clear();
        const int visible = 5;
        const float rowH = 58f;
        float y = list.Y + 12;
        int end = Math.Min(_items.Count, _scroll + visible);
        for (int i = _scroll; i < end; i++)
        {
            PlayerProfile profile = _items[i];
            var row = new RectF(list.X + 12, y, list.W - 24, rowH - 6);
            bool selected = i == _selected;
            c.FillRoundRect(row, 10, selected ? RowSelected : Row);
            if (selected)
            {
                c.StrokeRoundRect(row, 10, Gold, 1.2f);
            }

            bool active = profile.Id == _currentId;
            string name = profile.DisplayName;
            c.DrawText(name, new RectF(row.X + 18, row.Y + 9, row.W - 260, 26), Text,
                new TextStyle(18, Bold: active, Center: false));
            string meta = active ? L.T("app.ui.profile.active") : string.Empty;
            if (profile.IsSteam)
            {
                meta = meta.Length > 0 ? meta + " · Steam" : "Steam";
            }

            if (meta.Length > 0)
            {
                c.DrawText(meta, new RectF(row.Right - 250, row.Y + 11, 220, 24), active ? Gold : Muted,
                    new TextStyle(14, Center: false, Right: true));
            }

            _rowRects.Add((profile, row));
            y += rowH;
        }

        if (_items.Count == 0)
        {
            c.DrawText(L.T("app.ui.profile.empty"), new RectF(list.X, list.CenterY - 14, list.W, 28), Muted,
                new TextStyle(16, Center: true));
        }
    }

    private void DrawActions(ICanvas c)
    {
        float y = 548;
        _btnPrimary = new RectF(150, y, 170, 46);
        _btnNew = new RectF(340, y, 170, 46);
        _btnRename = new RectF(530, y, 170, 46);
        _btnDelete = new RectF(720, y, 170, 46);
        _btnBack = new RectF(910, y, 170, 46);

        bool hasSelection = _items.Count > 0;
        bool canDelete = hasSelection && _items.Count > 1 && _items[_selected].Id != _currentId;
        DrawButton(c, _btnPrimary, "app.ui.profile.switch", hasSelection, _hoverPrimary);
        DrawButton(c, _btnNew, "app.ui.profile.new", true, _hoverNew);
        DrawButton(c, _btnRename, "app.ui.profile.rename", hasSelection, _hoverRename);
        DrawButton(c, _btnDelete, "app.ui.profile.delete", canDelete, _hoverDelete, danger: true);
        DrawButton(c, _btnBack, "app.ui.profile.back", true, _hoverBack);
    }

    private void DrawEdit(ICanvas c)
    {
        var panel = new RectF(360, 300, 560, 180);
        c.FillRoundRect(panel, 16, Panel);
        c.StrokeRoundRect(panel, 16, Gold, 1.4f);
        c.DrawText(L.T(_creating ? "app.ui.profile.createTitle" : "app.ui.profile.renameTitle"),
            new RectF(panel.X + 24, panel.Y + 18, panel.W - 48, 26), Gold, new TextStyle(18, Bold: true, Center: false));

        var field = new RectF(panel.X + 24, panel.Y + 58, panel.W - 48, 44);
        c.FillRoundRect(field, 8, Color.Rgb(12, 20, 24));
        c.StrokeRoundRect(field, 8, PanelBorder, 1.2f);
        string shown = _editBuffer.Length == 0 ? L.T("app.ui.profile.nameHint") : _editBuffer;
        c.DrawText(shown, new RectF(field.X + 12, field.Y + 11, field.W - 24, 24),
            _editBuffer.Length == 0 ? Muted : Text, new TextStyle(17, Center: false));

        if (_editError.Length > 0)
        {
            c.DrawText(_editError, new RectF(panel.X + 24, panel.Y + 108, panel.W - 48, 20), Danger,
                new TextStyle(12, Center: false));
        }

        _btnSave = new RectF(panel.X + 180, panel.Bottom - 54, 90, 38);
        _btnCancel = new RectF(panel.X + 290, panel.Bottom - 54, 90, 38);
        DrawButton(c, _btnSave, "app.ui.profile.save", true, _hoverSave);
        DrawButton(c, _btnCancel, "app.ui.profile.cancel", true, _hoverCancel);
    }

    private void DrawConfirm(ICanvas c)
    {
        var panel = new RectF(390, 320, 500, 160);
        c.FillRoundRect(panel, 16, Panel);
        c.StrokeRoundRect(panel, 16, Danger, 1.4f);
        c.DrawText(L.T("app.ui.profile.deleteConfirm", ("name", _items[_selected].DisplayName)),
            new RectF(panel.X + 24, panel.Y + 34, panel.W - 48, 50), Text, new TextStyle(16, Center: true));
        _btnConfirm = new RectF(panel.X + 120, panel.Bottom - 54, 110, 38);
        _btnConfirmCancel = new RectF(panel.X + 270, panel.Bottom - 54, 110, 38);
        DrawButton(c, _btnConfirm, "app.ui.profile.confirmDelete", true, _hoverConfirm, danger: true);
        DrawButton(c, _btnConfirmCancel, "app.ui.profile.cancel", true, _hoverConfirmCancel);
    }

    private void DrawButton(ICanvas c, RectF rect, string key, bool enabled, bool hover, bool danger = false)
    {
        Color border = danger ? Danger : Gold;
        Color fill = hover && enabled ? Color.Rgb(52, 86, 78) : Color.Rgb(30, 48, 50);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, enabled ? border : Color.Rgb(58, 72, 70), enabled ? 1.4f : 1f);
        c.DrawText(L.T(key), rect, enabled ? (danger ? Danger : Text) : Muted,
            new TextStyle(15, Bold: true, Center: true));
    }

    public override bool OnPointerMove(float x, float y)
    {
        _hoverPrimary = _btnPrimary.Contains(x, y);
        _hoverNew = _btnNew.Contains(x, y);
        _hoverRename = _btnRename.Contains(x, y);
        _hoverDelete = _btnDelete.Contains(x, y);
        _hoverBack = _btnBack.Contains(x, y);
        _hoverSave = _btnSave.Contains(x, y);
        _hoverCancel = _btnCancel.Contains(x, y);
        _hoverConfirm = _btnConfirm.Contains(x, y);
        _hoverConfirmCancel = _btnConfirmCancel.Contains(x, y);
        bool overRow = _rowRects.Any(r => r.Rect.Contains(x, y));
        return overRow || _hoverPrimary || _hoverNew || _hoverRename || _hoverDelete || _hoverBack
            || _hoverSave || _hoverCancel || _hoverConfirm || _hoverConfirmCancel;
    }

    public override bool OnClick(float x, float y)
    {
        if (_editing)
        {
            if (_btnSave.Contains(x, y))
            {
                CommitEdit();
                return true;
            }

            if (_btnCancel.Contains(x, y))
            {
                CancelEdit();
                return true;
            }

            return true;
        }

        if (_confirmDelete)
        {
            if (_btnConfirm.Contains(x, y))
            {
                DeleteSelected();
                return true;
            }

            if (_btnConfirmCancel.Contains(x, y))
            {
                _confirmDelete = false;
                return true;
            }

            return true;
        }

        foreach (var (profile, rect) in _rowRects)
        {
            if (rect.Contains(x, y))
            {
                _selected = _items.ToList().FindIndex(p => p.Id == profile.Id);
                EnsureVisible();
                return true;
            }
        }

        if (_btnPrimary.Contains(x, y) && _items.Count > 0)
        {
            SwitchSelected();
            return true;
        }

        if (_btnNew.Contains(x, y))
        {
            BeginEdit(creating: true, initial: string.Empty);
            return true;
        }

        if (_btnRename.Contains(x, y) && _items.Count > 0)
        {
            BeginEdit(creating: false, initial: _items[_selected].DisplayName);
            return true;
        }

        if (_btnDelete.Contains(x, y) && _items.Count > 1 && _items[_selected].Id != _currentId)
        {
            _confirmDelete = true;
            return true;
        }

        if (_btnBack.Contains(x, y))
        {
            _back();
            return true;
        }

        return false;
    }

    public override bool OnKey(Keys key)
    {
        if (_editing)
        {
            if (key == Keys.Escape)
            {
                CancelEdit();
                return true;
            }

            if (key == Keys.Enter)
            {
                CommitEdit();
                return true;
            }

            if (key == Keys.Back && _editBuffer.Length > 0)
            {
                _editBuffer = _editBuffer[..^1];
                return true;
            }

            if (key == Keys.Up || key == Keys.Down)
            {
                return true;
            }

            return false;
        }

        if (_confirmDelete)
        {
            if (key == Keys.Escape)
            {
                _confirmDelete = false;
                return true;
            }

            return false;
        }

        if (key == Keys.Escape)
        {
            _back();
            return true;
        }

        if (key == Keys.Up)
        {
            Move(-1);
            return true;
        }

        if (key == Keys.Down)
        {
            Move(1);
            return true;
        }

        if (key == Keys.Enter)
        {
            if (_items.Count > 0)
            {
                SwitchSelected();
            }

            return true;
        }

        return false;
    }

    public override bool OnTextInput(string text)
    {
        if (!_editing || string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (char.IsControl(c) || _editBuffer.Length >= PlayerProfileStore.MaxNameLength)
            {
                continue;
            }

            _editBuffer += c;
        }

        _editError = string.Empty;
        return true;
    }

    private void BeginEdit(bool creating, string initial)
    {
        _creating = creating;
        _editing = true;
        _editBuffer = initial;
        _editError = string.Empty;
        _status = string.Empty;
    }

    private void CancelEdit()
    {
        _editing = false;
        _editBuffer = string.Empty;
        _editError = string.Empty;
    }

    private void CommitEdit()
    {
        string name = PlayerProfileStore.NormalizeName(_editBuffer, _items.Count + 1);
        if (name.Length == 0)
        {
            _editError = L.T("app.ui.profile.nameRequired");
            return;
        }

        try
        {
            if (_creating)
            {
                PlayerProfile created = _store.Create(name, activate: false);
                _editing = false;
                Reload();
                _selected = Math.Max(0, _items.ToList().FindIndex(p => p.Id == created.Id));
                _status = L.T("app.ui.profile.created", ("name", created.DisplayName));
                return;
            }

            PlayerProfile updated = _store.Rename(_items[_selected].Id, name);
            _editing = false;
            Reload();
            _selected = Math.Max(0, _items.ToList().FindIndex(p => p.Id == updated.Id));
            if (updated.Id == _currentId)
            {
                _nameChanged?.Invoke(updated.DisplayName);
            }

            _status = L.T("app.ui.profile.renamed", ("name", updated.DisplayName));
        }
        catch (Exception ex)
        {
            _editError = ex.Message;
        }
    }

    private void DeleteSelected()
    {
        if (_items.Count <= 1 || _items[_selected].Id == _currentId)
        {
            _confirmDelete = false;
            return;
        }

        try
        {
            string name = _items[_selected].DisplayName;
            _store.Delete(_items[_selected].Id);
            _confirmDelete = false;
            Reload();
            _status = L.T("app.ui.profile.deleted", ("name", name));
        }
        catch (Exception ex)
        {
            _confirmDelete = false;
            _status = ex.Message;
        }
    }

    private void SwitchSelected()
    {
        if (_items.Count == 0)
        {
            return;
        }

        PlayerProfile selected = _items[_selected];
        if (selected.Id == _currentId)
        {
            _back();
            return;
        }

        _switchProfile(selected);
    }

    private void Move(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(_selected + delta, 0, _items.Count - 1);
        EnsureVisible();
    }

    private void EnsureVisible()
    {
        const int visible = 5;
        if (_selected < _scroll)
        {
            _scroll = _selected;
        }
        else if (_selected >= _scroll + visible)
        {
            _scroll = _selected - visible + 1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _items.Count - visible));
    }
}
