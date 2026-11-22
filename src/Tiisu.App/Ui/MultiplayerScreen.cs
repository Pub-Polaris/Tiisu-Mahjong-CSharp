using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;
using Tiisu.App.Runtime;
using Tiisu.Localization;
using Tiisu.Net;

namespace Tiisu.App.Ui;

/// <summary>
/// A–D 四区 × 每区 2 桌的局域网大厅。
/// 每个桌位由一个 Host 创建；Client 只发现/加入，不计算牌局。
/// </summary>
public sealed class MultiplayerScreen : Screen
{
    private readonly Func<string, string, int, int, bool, Task<HostEngine>> _createHost;
    private readonly Func<RoomInfo, Task<(PeerClient Client, NetworkClientSession Session)>> _joinRoom;
    private readonly Func<Task> _leaveRoom;
    private readonly Action<HostEngine> _onHostStarted;
    private readonly Action<PeerClient, NetworkClientSession> _onJoined;
    private readonly Action<string> _savePlayerName;
    private readonly LanDiscovery _discovery = new();
    private readonly List<RoomInfo> _rooms = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private HostEngine? _host;
    private PeerClient? _client;
    private NetworkClientSession? _clientSession;
    private RoomInfo? _joinedRoom;
    private readonly List<ChatMessage> _chat = new();
    private string _playerName = "";
    private string _transport = "tcp";
    private string _zone = "A";
    private int _mode = 1;
    private bool _aiFill = true;
    private string _status = "正在查询局域网…";
    private string _chatDraft = "";
    private bool _editingName;
    private bool _editingChat;
    private bool _searching;
    private bool _joining;
    private bool _leaving;
    private double _nextRefresh;
    private float _mouseX;
    private float _mouseY;

    public MultiplayerScreen(
        string playerName,
        Func<string, string, int, int, bool, Task<HostEngine>> createHost,
        Func<RoomInfo, Task<(PeerClient Client, NetworkClientSession Session)>> joinRoom,
        Func<Task> leaveRoom,
        Action<HostEngine> onHostStarted,
        Action<PeerClient, NetworkClientSession> onJoined,
        Action<string> savePlayerName) : base("multiplayer")
    {
        _playerName = playerName;
        _createHost = createHost;
        _joinRoom = joinRoom;
        _leaveRoom = leaveRoom;
        _onHostStarted = onHostStarted;
        _onJoined = onJoined;
        _savePlayerName = savePlayerName;
    }

    public void SetStatus(string status) => _status = status;

    public void ResetRoom()
    {
        _host = null;
        _client = null;
        _clientSession = null;
        _joinedRoom = null;
        _chat.Clear();
        _chatDraft = "";
        _editingChat = false;
        _rooms.Clear();
        _status = "正在查询局域网…";
        _nextRefresh = 0;
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
                _status = rooms.Count == 0 ? "没有发现房间" : $"发现 {rooms.Count} 个桌位";
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
            Color.Rgb(16, 26, 30), Color.Rgb(7, 11, 14));

        c.DrawText(UiText.Tr("局域网联机"),
            new RectF(0, 20, Width, 42), Color.Rgb(242, 230, 190),
            new TextStyle(30, Center: true));

        DrawPlayerName(c);

        for (int i = 0; i < RoomInfo.Zones.Length; i++)
        {
            DrawZone(c, RoomInfo.Zones[i], i);
        }

        if (_host is null && _client is null)
        {
            DrawHostSettings(c);
        }

        if (_host is not null)
        {
            DrawHostRoom(c);
        }
        else if (_client is not null)
        {
            DrawClientRoom(c);
        }
        else
        {
            DrawButton(c, BackRect, "返回", Color.Rgb(34, 52, 56));
            c.DrawText(_status, new RectF(0, 674, Width, 26), Color.Rgb(159, 180, 170),
                new TextStyle(14, Center: true));
        }
    }

    private void DrawPlayerName(ICanvas c)
    {
        c.DrawText("玩家名", new RectF(34, 76, 58, 28), Color.Rgb(190, 210, 196), new TextStyle(14));
        var rect = NameRect;
        c.FillRoundRect(rect, 8, _editingName ? Color.Rgb(52, 86, 78) : Color.Rgb(30, 44, 48));
        c.StrokeRoundRect(rect, 8, Color.Rgb(140, 170, 150), 1.2f);
        c.DrawText(string.IsNullOrWhiteSpace(_playerName) ? Environment.UserName : _playerName,
            rect, Color.Rgb(240, 236, 220), new TextStyle(16));
    }

    private void DrawZone(ICanvas c, string zone, int zoneIndex)
    {
        var group = ZoneRect(zoneIndex);
        c.DrawText(zone + " 区", new RectF(group.X, group.Y, group.W, 26),
            Color.Rgb(242, 210, 128), new TextStyle(20, Bold: true));
        for (int table = 1; table <= RoomInfo.TablesPerZone; table++)
        {
            var slot = SlotRect(zoneIndex, table - 1);
            RoomInfo? room = FindRoom(zone, table);
            bool hover = slot.Contains(_mouseX, _mouseY);
            Color fill = room is null
                ? Color.Rgb(25, 36, 40)
                : hover ? Color.Rgb(52, 86, 78) : Color.Rgb(34, 52, 56);
            c.FillRoundRect(slot, 10, fill);
            c.StrokeRoundRect(slot, 10, Color.Rgb(140, 172, 150), 1.2f);
            string label = room is null
                ? $"{zone}{table}  空闲"
                : $"{zone}{table}  {room.HostName}  {room.Players}/{room.MaxPlayers}  [{RoomInfo.TransportLabel(room.Transport)}]";
            c.DrawText(UiText.Tr(label), new RectF(slot.X + 12, slot.Y + 12, slot.W - 24, 28),
                room is null ? Color.Rgb(130, 150, 142) : Color.Rgb(240, 236, 220),
                new TextStyle(14, Bold: room is not null));
            if (room is not null)
            {
                c.DrawText(room.State == "playing" ? "对局中" : "等待中",
                    new RectF(slot.X + 12, slot.Y + 42, slot.W - 24, 20),
                    Color.Rgb(170, 195, 180), new TextStyle(12));
            }
        }
    }

    private void DrawHostSettings(ICanvas c)
    {
        var panel = SettingsRect;
        c.FillRoundRect(panel, 10, Color.Rgb(24, 38, 40));
        c.StrokeRoundRect(panel, 10, Color.Rgb(100, 132, 112), 1.1f);
        c.DrawText("房主设定", new RectF(panel.X + 12, panel.Y + 8, 150, 26),
            Color.Rgb(242, 210, 128), new TextStyle(16, Bold: true));

        c.DrawText("区域", new RectF(panel.X + 12, panel.Y + 42, 50, 22),
            Color.Rgb(180, 205, 190), new TextStyle(13));
        for (int i = 0; i < RoomInfo.Zones.Length; i++)
        {
            DrawSmallButton(c, ZoneButtonRect(i), RoomInfo.Zones[i],
                string.Equals(_zone, RoomInfo.Zones[i], StringComparison.Ordinal));
        }

        c.DrawText("协议", new RectF(panel.X + 12, panel.Y + 78, 50, 22),
            Color.Rgb(180, 205, 190), new TextStyle(13));
        DrawSmallButton(c, TcpRect, "TCP", _transport == "tcp");
        DrawSmallButton(c, WsRect, "WS", _transport == "websocket");

        c.DrawText("模式", new RectF(panel.X + 12, panel.Y + 114, 50, 22),
            Color.Rgb(180, 205, 190), new TextStyle(13));
        DrawSmallButton(c, ModeRect, ModeLabel(_mode), true);

        c.DrawText("AI 补位", new RectF(panel.X + 12, panel.Y + 150, 70, 22),
            Color.Rgb(180, 205, 190), new TextStyle(13));
        DrawSmallButton(c, AiRect, _aiFill ? "开" : "关", _aiFill);

        DrawSmallButton(c, CreateRect, "创建房间", true);
        if (_host is not null)
        {
            DrawSmallButton(c, StartRect, "开始对局", true);
        }
    }

    private void DrawHostRoom(ICanvas c)
    {
        var panel = SettingsRect;
        c.FillRoundRect(panel, 10, Color.Rgb(24, 38, 40));
        c.StrokeRoundRect(panel, 10, Color.Rgb(100, 132, 112), 1.1f);
        c.DrawText($"房间 {_host!.Room.SlotLabel}  ·  {RoomInfo.TransportLabel(_host.Transport)}",
            new RectF(panel.X + 12, panel.Y + 8, panel.W - 24, 26),
            Color.Rgb(242, 210, 128), new TextStyle(15, Bold: true));
        float y = panel.Y + 42;
        foreach (var player in _host.Lobby)
        {
            string suffix = player.Seat == PeerServer.HostSeat
                ? "房主"
                : player.Ready ? "已确认" : "等待确认";
            c.DrawText($"{player.Seat + 1}. {player.Name}  [{suffix}]",
                new RectF(panel.X + 20, y, panel.W - 40, 24),
                player.Seat == PeerServer.HostSeat || player.Ready
                    ? Color.Rgb(220, 220, 200)
                    : Color.Rgb(190, 160, 110),
                new TextStyle(14));
            y += 24;
        }

        c.DrawText(_host.Lobby.Count < NetworkConstants.MaxPlayers && _host.Room.AiFill
                ? "空位由 AI 补位"
                : _host.Lobby.Count < NetworkConstants.MaxPlayers ? "等待玩家" : "四人已到齐",
            new RectF(panel.X + 12, panel.Bottom - 70, panel.W - 24, 20),
            Color.Rgb(159, 180, 170), new TextStyle(13));
        DrawSmallButton(c, StartRect, "开始对局", true);
        DrawButton(c, BackRect, "返回", Color.Rgb(34, 52, 56));
        DrawChat(c);
    }

    private void DrawClientRoom(ICanvas c)
    {
        var panel = SettingsRect;
        c.FillRoundRect(panel, 10, Color.Rgb(24, 38, 40));
        c.StrokeRoundRect(panel, 10, Color.Rgb(100, 132, 112), 1.1f);
        RoomInfo room = _joinedRoom ?? _client!.Room ?? RoomInfo.Create("房间", "Host", 0, "v");
        c.DrawText($"房间 {room.SlotLabel}  ·  {RoomInfo.TransportLabel(room.Transport)}",
            new RectF(panel.X + 12, panel.Y + 8, panel.W - 24, 26),
            Color.Rgb(242, 210, 128), new TextStyle(15, Bold: true));
        float y = panel.Y + 42;
        foreach (PlayerLobbyInfo player in _client!.Players)
        {
            string suffix = player.Seat == PeerServer.HostSeat
                ? "房主"
                : player.Ready ? "已确认" : "未确认";
            c.DrawText($"{player.Seat + 1}. {player.Name}  [{suffix}]",
                new RectF(panel.X + 20, y, panel.W - 40, 24),
                player.Ready || player.Seat == PeerServer.HostSeat
                    ? Color.Rgb(220, 220, 200)
                    : Color.Rgb(190, 160, 110),
                new TextStyle(14));
            y += 24;
        }

        c.DrawText("等待房主开始；可先确认准备", new RectF(panel.X + 12, panel.Bottom - 120, panel.W - 24, 20),
            Color.Rgb(159, 180, 170), new TextStyle(13));
        DrawSmallButton(c, ReadyRect, _client.IsReady ? "取消确认" : "确认准备", true);
        DrawButton(c, BackRect, "返回", Color.Rgb(34, 52, 56));
        DrawChat(c);
    }

    private void DrawChat(ICanvas c)
    {
        var panel = ChatRect;
        c.FillRoundRect(panel, 10, Color.Rgb(22, 34, 38));
        c.StrokeRoundRect(panel, 10, Color.Rgb(90, 120, 104), 1.0f);
        c.DrawText("大厅对话", new RectF(panel.X + 12, panel.Y + 8, 160, 22),
            Color.Rgb(242, 210, 128), new TextStyle(14, Bold: true));

        int start = Math.Max(0, _chat.Count - 4);
        float y = panel.Y + 34;
        for (int i = start; i < _chat.Count; i++)
        {
            ChatMessage message = _chat[i];
            c.DrawText($"{message.From}: {message.Text}",
                new RectF(panel.X + 14, y, panel.W - 28, 20),
                Color.Rgb(205, 218, 205), new TextStyle(12));
            y += 20;
        }

        c.FillRoundRect(ChatInputRect, 6, _editingChat ? Color.Rgb(52, 86, 78) : Color.Rgb(30, 44, 48));
        c.StrokeRoundRect(ChatInputRect, 6, Color.Rgb(120, 150, 130), 1.0f);
        string draft = _chatDraft.Length == 0 ? "输入消息，Enter 发送" : _chatDraft;
        c.DrawText(draft, ChatInputRect, _chatDraft.Length == 0
            ? Color.Rgb(120, 140, 130)
            : Color.Rgb(240, 236, 220), new TextStyle(13));
        DrawSmallButton(c, ChatSendRect, "发送", _chatDraft.Length > 0);
    }

    private void DrawSmallButton(ICanvas c, RectF rect, string text, bool active)
    {
        bool hover = rect.Contains(_mouseX, _mouseY);
        c.FillRoundRect(rect, 8, active
            ? hover ? Color.Rgb(80, 120, 98) : Color.Rgb(52, 86, 78)
            : Color.Rgb(30, 44, 48));
        c.StrokeRoundRect(rect, 8, Color.Rgb(140, 170, 150), 1.0f);
        c.DrawText(UiText.Tr(text), rect, Color.Rgb(235, 230, 210), new TextStyle(13, Center: true));
    }

    private void DrawButton(ICanvas c, RectF rect, string text, Color fill)
    {
        bool hover = rect.Contains(_mouseX, _mouseY);
        c.FillRoundRect(rect, 10, hover ? Color.Rgb(70, 108, 92) : fill);
        c.StrokeRoundRect(rect, 10, Color.Rgb(196, 168, 96), 1.2f);
        c.DrawText(UiText.Tr(text), rect, Color.Rgb(240, 236, 220),
            new TextStyle(18, Bold: true, Center: true));
    }

    private static readonly RectF NameRect = new(100, 72, 240, 34);
    private static readonly RectF BackRect = new(40, 620, 150, 48);
    private static readonly RectF SettingsRect = new(900, 130, 340, 470);
    private static readonly RectF TcpRect = new(960, 204, 80, 30);
    private static readonly RectF WsRect = new(1050, 204, 80, 30);
    private static readonly RectF ModeRect = new(960, 240, 170, 30);
    private static readonly RectF AiRect = new(1020, 276, 80, 30);
    private static readonly RectF CreateRect = new(960, 320, 240, 40);
    private static readonly RectF StartRect = new(960, 520, 240, 40);
    private static readonly RectF ReadyRect = new(960, 520, 240, 40);
    private static readonly RectF ChatRect = new(36, 470, 840, 150);
    private static readonly RectF ChatInputRect = new(48, 574, 700, 30);
    private static readonly RectF ChatSendRect = new(756, 574, 108, 30);

    private static RectF ZoneButtonRect(int index) => new(960 + index * 52, 168, 44, 28);

    private static RectF ZoneRect(int index) => new(36 + index * 210, 130, 190, 320);

    private static RectF SlotRect(int zoneIndex, int tableIndex) =>
        new(48 + zoneIndex * 210, 170 + tableIndex * 78, 166, 70);

    private static string ModeLabel(int mode) => mode switch
    {
        4 => "东风场",
        8 => "南风场",
        16 => "全庄",
        _ => "一局",
    };

    private RoomInfo? FindRoom(string zone, int table)
    {
        if (_host is not null
            && string.Equals(RoomInfo.NormalizeZone(_host.Room.Zone), zone, StringComparison.Ordinal)
            && Math.Clamp(_host.Room.Table, 1, RoomInfo.TablesPerZone) == table)
        {
            return _host.Room;
        }

        return _rooms.FirstOrDefault(r =>
            string.Equals(RoomInfo.NormalizeZone(r.Zone), zone, StringComparison.Ordinal)
            && Math.Clamp(r.Table, 1, RoomInfo.TablesPerZone) == table);
    }

    public override bool OnPointerMove(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
        return true;
    }

    public override bool OnClick(float x, float y)
    {
        if (NameRect.Contains(x, y))
        {
            _editingName = true;
            _editingChat = false;
            return true;
        }

        if (_host is not null || _client is not null)
        {
            if (ChatInputRect.Contains(x, y))
            {
                _editingChat = true;
                _editingName = false;
                return true;
            }

            if (ChatSendRect.Contains(x, y))
            {
                SendChat();
                return true;
            }
        }

        if (_client is not null && ReadyRect.Contains(x, y))
        {
            _ = ToggleReadyAsync();
            return true;
        }

        for (int i = 0; i < RoomInfo.Zones.Length; i++)
        {
            if (ZoneButtonRect(i).Contains(x, y))
            {
                _zone = RoomInfo.Zones[i];
                return true;
            }
        }

        if (TcpRect.Contains(x, y))
        {
            _transport = "tcp";
            return true;
        }

        if (WsRect.Contains(x, y))
        {
            _transport = "websocket";
            return true;
        }

        if (ModeRect.Contains(x, y))
        {
            _mode = _mode switch { 1 => 4, 4 => 8, 8 => 16, _ => 1 };
            return true;
        }

        if (AiRect.Contains(x, y))
        {
            _aiFill = !_aiFill;
            return true;
        }

        if (CreateRect.Contains(x, y))
        {
            _ = CreateHostAsync();
            return true;
        }

        if (_host is not null && StartRect.Contains(x, y))
        {
            if (!_host.StartMatch(_mode))
            {
                _status = _host.LastError ?? "无法开始对局";
                return true;
            }

            _onHostStarted(_host);
            _host = null;
            return true;
        }

        if (BackRect.Contains(x, y))
        {
            _ = LeaveRoomAsync();
            return true;
        }

        for (int zone = 0; zone < RoomInfo.Zones.Length; zone++)
        {
            for (int table = 1; table <= RoomInfo.TablesPerZone; table++)
            {
                if (!SlotRect(zone, table - 1).Contains(x, y))
                {
                    continue;
                }

                RoomInfo? room = FindRoom(RoomInfo.Zones[zone], table);
                if (room is not null && _host is null && _client is null)
                {
                    _ = JoinAsync(room);
                }

                return true;
            }
        }

        return true;
    }

    public override bool OnKey(Keys key) => OnKey(key, false);

    public override bool OnTextInput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (_editingName)
        {
            int room = NetworkConstants.MaxNameLength - _playerName.Length;
            if (room > 0)
            {
                _playerName += text.Length <= room ? text : text[..room];
            }

            return true;
        }

        if (_editingChat)
        {
            int room = NetworkConstants.MaxChatLength - _chatDraft.Length;
            if (room > 0)
            {
                _chatDraft += text.Length <= room ? text : text[..room];
            }

            return true;
        }

        return false;
    }

    public override bool OnKey(Keys key, bool shift)
    {
        if (_editingName)
        {
            if (key == Keys.Back)
            {
                if (_playerName.Length > 0)
                {
                    _playerName = _playerName[..^1];
                }

                return true;
            }

            if (key == Keys.Enter || key == Keys.Escape)
            {
                _editingName = false;
                _savePlayerName(_playerName);
                return true;
            }

            string? text = KeyText(key, shift);
            if (text is not null && _playerName.Length < NetworkConstants.MaxNameLength)
            {
                _playerName += text;
                return true;
            }

            return false;
        }

        if (_editingChat)
        {
            if (key == Keys.Back)
            {
                if (_chatDraft.Length > 0)
                {
                    _chatDraft = _chatDraft[..^1];
                }

                return true;
            }

            if (key == Keys.Enter)
            {
                SendChat();
                return true;
            }

            if (key == Keys.Escape)
            {
                _editingChat = false;
                return true;
            }

            string? chatText = KeyText(key, shift);
            if (chatText is not null && _chatDraft.Length < NetworkConstants.MaxChatLength)
            {
                _chatDraft += chatText;
                return true;
            }

            return false;
        }

        if (key == Keys.Escape)
        {
            _ = LeaveRoomAsync();
            return true;
        }

        return false;
    }

    private static string? KeyText(Keys key, bool shift)
    {
        if (key is >= Keys.A and <= Keys.Z)
        {
            char c = (char)('A' + (key - Keys.A));
            return shift ? c.ToString() : char.ToLowerInvariant(c).ToString();
        }

        if (key is >= Keys.D0 and <= Keys.D9)
        {
            return ((char)('0' + (key - Keys.D0))).ToString();
        }

        return key == Keys.Space ? " " : null;
    }

    private async Task CreateHostAsync()
    {
        if (_joining || _leaving || _host is not null)
        {
            return;
        }

        string zone = RoomInfo.NormalizeZone(_zone);
        int? table = RoomInfo.FirstFreeTable(zone, _rooms);
        if (table is null)
        {
            _status = zone + " 区已满";
            return;
        }

        _joining = true;
        try
        {
            _savePlayerName(_playerName);
            _host = await _createHost(_transport, zone, table.Value, _mode, _aiFill);
            _host.StartServer();
            _host.ChatReceived += message => Post(() => AddChat(message));
            _host.LobbyChanged += _ => Post(() => { });
            _status = $"房间 {zone}{table.Value} 已创建";
        }
        catch (Exception ex)
        {
            _status = "创建房间失败: " + ex.Message;
        }
        finally
        {
            _joining = false;
        }
    }

    private async Task JoinAsync(RoomInfo room)
    {
        if (_joining || _leaving || _host is not null)
        {
            return;
        }

        _joining = true;
        _status = "正在加入 " + room.SlotLabel + "…";
        try
        {
            _savePlayerName(_playerName);
            var joined = await _joinRoom(room);
            _client = joined.Client;
            _clientSession = joined.Session;
            _joinedRoom = room;
            _chat.Clear();
            _client.ChatReceived += message => Post(() => AddChat(message));
            _client.MatchStarting += () =>
            {
                PeerClient client = joined.Client;
                NetworkClientSession session = joined.Session;
                Post(() => _onJoined(client, session));
            };
            _client.Disconnected += reason => Post(() =>
            {
                _status = "连接断开: " + reason;
                _client = null;
                _clientSession = null;
            });
            _status = "已加入 " + room.SlotLabel + "，等待房主开始";
        }
        catch (Exception ex)
        {
            _joining = false;
            _status = "加入失败: " + ex.Message;
            return;
        }

        _joining = false;
    }

    private void AddChat(ChatMessage message)
    {
        _chat.Add(message);
        if (_chat.Count > 50)
        {
            _chat.RemoveAt(0);
        }
    }

    private void SendChat()
    {
        string text = _chatDraft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (_host is not null)
        {
            _host.SendChat(text);
            AddChat(new ChatMessage(PeerServer.HostSeat, _host.Room.HostName,
                text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        else if (_client is not null)
        {
            _ = _client.SendChatAsync(text);
        }

        _chatDraft = "";
        _editingChat = false;
    }

    private async Task ToggleReadyAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            await _client.SetReadyAsync(!_client.IsReady);
        }
        catch (Exception ex)
        {
            _status = "确认失败: " + ex.Message;
        }
    }

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
            _client = null;
            _clientSession = null;
            _joinedRoom = null;
            await _leaveRoom();
            _rooms.Clear();
            _chat.Clear();
            _status = "正在查询局域网…";
            _nextRefresh = 0;
        }
        catch (Exception ex)
        {
            _status = "离开房间失败: " + ex.Message;
        }
        finally
        {
            _leaving = false;
        }
    }

    public void Dispose()
    {
        _host?.Dispose();
        _host = null;
        _discovery.Dispose();
    }
}
