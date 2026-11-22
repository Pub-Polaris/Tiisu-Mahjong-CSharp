using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;
using Tiisu.App.Runtime;
using Tiisu.Localization;
using Tiisu.Net;

namespace Tiisu.App.Ui;

/// <summary>
/// 局域网联机页：Host 创建房间、Client 自动查询并加入。
/// 所有网络操作只通过 HostEngine/PeerClient，不在这里访问 Tiisu.Engine。
/// </summary>
public sealed class MultiplayerScreen : Screen
{
    private readonly Func<Task> _leaveRoom;
    private readonly Func<Task<HostEngine>> _createHost;
    private readonly Func<RoomInfo, Task<(PeerClient Client, NetworkClientSession Session)>> _joinRoom;
    private readonly Action<HostEngine> _onHostStarted;
    private readonly Action<PeerClient, NetworkClientSession> _onJoined;
    private readonly LanDiscovery _discovery = new();
    private readonly List<RoomInfo> _rooms = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private HostEngine? _host;
    private string _status = "正在查询局域网…";
    private bool _searching;
    private bool _joining;
    private bool _leaving;
    private double _nextRefresh;

    public MultiplayerScreen(
        Func<Task> leaveRoom,
        Func<Task<HostEngine>> createHost,
        Func<RoomInfo, Task<(PeerClient Client, NetworkClientSession Session)>> joinRoom,
        Action<HostEngine> onHostStarted,
        Action<PeerClient, NetworkClientSession> onJoined) : base("multiplayer")
    {
        _leaveRoom = leaveRoom;
        _createHost = createHost;
        _joinRoom = joinRoom;
        _onHostStarted = onHostStarted;
        _onJoined = onJoined;
    }

    public override void Update(double dt)
    {
        if (_host is not null || _joining)
        {
            return;
        }

        _nextRefresh -= dt;
        if (_nextRefresh <= 0 && !_searching)
        {
            _nextRefresh = 2.0;
            _ = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_searching)
        {
            return;
        }

        _searching = true;
        try
        {
            IReadOnlyList<RoomInfo> rooms = await _discovery.SearchAsync(TimeSpan.FromMilliseconds(600));
            Post(() =>
            {
                _rooms.Clear();
                _rooms.AddRange(rooms);
                _status = rooms.Count == 0 ? "没有发现房间" : $"发现 {rooms.Count} 个房间";
            });
        }
        catch (Exception ex)
        {
            Post(() => _status = "查询失败: " + ex.Message);
        }
        finally
        {
            _searching = false;
        }
    }

    private void Post(Action action)
    {
        if (_ui is null)
        {
            action();
            return;
        }

        _ui.Post(_ => action(), null);
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 28, 30), Color.Rgb(8, 12, 16));

        c.DrawText(UiText.Tr(_host is null ? "局域网联机" : "联机房间"),
            new RectF(0, 42, Width, 48), Color.Rgb(242, 230, 190),
            new TextStyle(34, Center: true));

        if (_host is not null)
        {
            DrawHost(c);
            return;
        }

        c.DrawText(_status, new RectF(0, 108, Width, 26), Color.Rgb(159, 180, 170),
            new TextStyle(15, Center: true));

        DrawButton(c, CreateRect, "创建房间", Color.Rgb(52, 86, 78));
        DrawButton(c, RefreshRect, "刷新局域网", Color.Rgb(34, 52, 56));
        DrawButton(c, BackRect, "返回", Color.Rgb(34, 52, 56));

        float y = 230;
        foreach (RoomInfo room in _rooms.Take(6))
        {
            var rect = new RectF(300, y, 680, 50);
            bool hover = rect.Contains(_mouseX, _mouseY);
            c.FillRoundRect(rect, 10, hover ? Color.Rgb(52, 86, 78) : Color.Rgb(34, 52, 56));
            c.StrokeRoundRect(rect, 10, Color.Rgb(196, 168, 96), 1.2f);
            c.DrawText($"{room.RoomName}  ·  {room.HostName}  ·  {room.Players}/{room.MaxPlayers}",
                new RectF(rect.X + 16, rect.Y + 14, rect.W - 32, 24),
                Color.Rgb(240, 236, 220), new TextStyle(16));
            y += 58;
        }

        if (_rooms.Count == 0)
        {
            c.DrawText("同一局域网的主机会自动出现在这里",
                new RectF(0, 300, Width, 30), Color.Rgb(127, 145, 137),
                new TextStyle(15, Center: true));
        }
    }

    private void DrawHost(ICanvas c)
    {
        c.DrawText($"房间：{_host!.Room.RoomName}  ·  端口 {_host.Port}",
            new RectF(0, 108, Width, 28), Color.Rgb(240, 236, 220),
            new TextStyle(16, Center: true));

        float y = 180;
        foreach (var player in _host.Lobby)
        {
            c.DrawText($"{player.Seat + 1}. {player.Name}", new RectF(320, y, 640, 28),
                Color.Rgb(220, 220, 200), new TextStyle(18));
            y += 36;
        }

        string hint = _host.Lobby.Count < NetworkConstants.MaxPlayers
            ? $"当前 {_host.Lobby.Count} 人，空位由 Host AI 补位"
            : "四人已到齐";
        c.DrawText(hint, new RectF(0, 400, Width, 28), Color.Rgb(159, 180, 170),
            new TextStyle(16, Center: true));

        DrawButton(c, StartRect, "开始对局", Color.Rgb(52, 86, 78));
        DrawButton(c, BackRect, "返回", Color.Rgb(34, 52, 56));
    }

    private static readonly RectF CreateRect = new(390, 150, 190, 54);
    private static readonly RectF RefreshRect = new(600, 150, 190, 54);
    private static readonly RectF BackRect = new(40, 630, 150, 50);
    private static readonly RectF StartRect = new(490, 470, 300, 62);

    private void DrawButton(ICanvas c, RectF rect, string text, Color fill)
    {
        bool hover = rect.Contains(_mouseX, _mouseY);
        c.FillRoundRect(rect, 10, hover ? Color.Rgb(70, 108, 92) : fill);
        c.StrokeRoundRect(rect, 10, Color.Rgb(196, 168, 96), 1.2f);
        c.DrawText(UiText.Tr(text), rect, Color.Rgb(240, 236, 220),
            new TextStyle(18, Bold: true, Center: true));
    }

    private float _mouseX;
    private float _mouseY;

    public void SetStatus(string status) => _status = status;

    /// <summary>ScreenHost 关闭网络会话后清除本地房间引用，避免再次进入显示旧房间。</summary>
    public void ResetRoom()
    {
        _host = null;
        _rooms.Clear();
        _status = "正在查询局域网…";
        _nextRefresh = 0;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
        return true;
    }

    public override bool OnClick(float x, float y)
    {
        if (_host is not null)
        {
            if (StartRect.Contains(x, y))
            {
                _host.StartMatch();
                _onHostStarted(_host);
                return true;
            }

            if (BackRect.Contains(x, y))
            {
                _ = LeaveRoomAsync();
                return true;
            }

            return true;
        }

        if (BackRect.Contains(x, y))
        {
            _ = LeaveRoomAsync();
            return true;
        }

        if (CreateRect.Contains(x, y))
        {
            _ = CreateHostAsync();
            return true;
        }

        if (RefreshRect.Contains(x, y))
        {
            _nextRefresh = 0;
            _ = RefreshAsync();
            return true;
        }

        float rowY = 230;
        foreach (RoomInfo room in _rooms.Take(6))
        {
            if (new RectF(300, rowY, 680, 50).Contains(x, y))
            {
                _ = JoinAsync(room);
                return true;
            }

            rowY += 58;
        }

        return true;
    }

    private async Task CreateHostAsync()
    {
        try
        {
            _host = await _createHost();
            _host.StartServer();
            _status = "房间已创建，等待局域网玩家加入";
            _host.LobbyChanged += _ => Post(() => { });
        }
        catch (Exception ex)
        {
            _status = "创建房间失败: " + ex.Message;
        }
    }

    private async Task JoinAsync(RoomInfo room)
    {
        if (_joining)
        {
            return;
        }

        _joining = true;
        _status = "正在加入 " + room.RoomName + "…";
        try
        {
            var joined = await _joinRoom(room);
            _onJoined(joined.Client, joined.Session);
        }
        catch (Exception ex)
        {
            _joining = false;
            _status = "加入失败: " + ex.Message;
        }
    }

    /// <summary>离开房间：停止自己拥有的房间，或让 ScreenHost 关闭客户端会话。</summary>
    public async Task LeaveRoomAsync()
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        try
        {
            _host = null;
            await _leaveRoom();
            _rooms.Clear();
            _status = "正在查询局域网…";
            _nextRefresh = 0;
        }
        finally
        {
            _leaving = false;
        }
    }

    public override bool OnKey(Keys key)
    {
        if (key == Keys.Escape)
        {
            _ = LeaveRoomAsync();
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_host is not null)
        {
            _host.Dispose();
            _host = null;
        }

        _discovery.Dispose();
    }
}
