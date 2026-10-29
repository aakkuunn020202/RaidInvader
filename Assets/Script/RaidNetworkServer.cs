using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

public class RaidNetworkServer : MonoBehaviour
{
    // ゲームから参照するサーバーインスタンス
    public static RaidNetworkServer Instance { get; private set; }

    [Header("サーバー設定")]
    [SerializeField] private int port = 8080; // 待ち受けポート番号
    // スマホ接続用に表示するURL
    public string ServerUrl { get; private set; }

    private TcpListener tcpListener; // TCP待ち受け本体
    private Thread listenerThread; // 接続受付スレッド
    private volatile bool isRunning; // サーバー稼働状態

    private readonly ConcurrentDictionary<string, TcpClient> clients = new ConcurrentDictionary<string, TcpClient>();
    // 受信メッセージ（クライアントID, JSON）
    public ConcurrentQueue<(string id, string json)> MessageQueue { get; } = new ConcurrentQueue<(string, string)>();
    // 参加・退出イベント
    public ConcurrentQueue<string> JoinQueue { get; } = new ConcurrentQueue<string>();
    public ConcurrentQueue<string> LeaveQueue { get; } = new ConcurrentQueue<string>();

    // シングルトン初期化
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        Application.runInBackground = true;
    }

    // 起動時にサーバー開始
    private void Start()
    {
        StartServer();
    }

    // 破棄時にサーバー停止
    private void OnDestroy()
    {
        StopServer();
    }

    private void StartServer()
    {
        try
        {
            // TCPリスナーを起動し、別スレッドで接続待ちを開始
            isRunning = true;
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();

            listenerThread = new Thread(ListenLoop) { IsBackground = true };
            listenerThread.Start();

            ServerUrl = $"http://{GetLocalIPAddress()}:{port}";
            Debug.Log($"[RaidServer] 起動完了: {ServerUrl}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RaidServer] 起動失敗: {e.Message}");
        }
    }

    // サーバー停止と接続切断処理
    private void StopServer()
    {
        isRunning = false;
        try { tcpListener?.Stop(); } catch { }
        foreach (var kvp in clients)
        {
            try { kvp.Value.Close(); } catch { }
        }
        clients.Clear();
    }

    private void ListenLoop()
    {
        // 接続ごとにワーカースレッドへ振り分け
        while (isRunning)
        {
            try
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                client.NoDelay = true;
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
            catch (SocketException) { break; }
            catch (Exception) { }
        }
    }

    private void HandleClient(object obj)
    {
        // HTTP要求を解析し、WebSocketなら接続維持、通常HTTPならコントローラー画面を返す
        TcpClient client = (TcpClient)obj;
        NetworkStream stream = client.GetStream();
        StreamReader reader = new StreamReader(stream, Encoding.UTF8);

        try
        {
            string requestLine = reader.ReadLine();
            if (string.IsNullOrEmpty(requestLine)) { client.Close(); return; }

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
            {
                int sep = line.IndexOf(':');
                if (sep > 0) headers[line.Substring(0, sep).Trim()] = line.Substring(sep + 1).Trim();
            }

            if (headers.ContainsKey("Upgrade") && headers["Upgrade"].Equals("websocket", StringComparison.OrdinalIgnoreCase))
            {
                string acceptKey = ComputeWebSocketAcceptKey(headers["Sec-WebSocket-Key"]);
                string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                                  "Upgrade: websocket\r\n" +
                                  "Connection: Upgrade\r\n" +
                                  $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
                byte[] resBytes = Encoding.UTF8.GetBytes(response);
                stream.Write(resBytes, 0, resBytes.Length);

                string clientId = "P-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
                clients[clientId] = client;
                JoinQueue.Enqueue(clientId);

                ReceiveWebSocketLoop(clientId, client, stream);
            }
            else
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(GetControllerHtml());
                string header = "HTTP/1.1 200 OK\r\n" +
                                "Content-Type: text/html; charset=utf-8\r\n" +
                                $"Content-Length: {bodyBytes.Length}\r\n" +
                                "Connection: close\r\n\r\n";
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
                client.Close();
            }
        }
        catch (Exception)
        {
            client.Close();
        }
    }

    private void ReceiveWebSocketLoop(string clientId, TcpClient client, NetworkStream stream)
    {
        // WebSocketフレームを読み取り、テキストフレームを入力キューへ積む
        try
        {
            while (isRunning && client.Connected)
            {
                int b0 = stream.ReadByte();
                int b1 = stream.ReadByte();
                if (b0 == -1 || b1 == -1) break;

                int opcode = b0 & 0x0F;
                if (opcode == 8) break;

                bool masked = (b1 & 0x80) != 0;
                int payloadLen = b1 & 0x7F;
                if (payloadLen == 126) payloadLen = (stream.ReadByte() << 8) | stream.ReadByte();

                byte[] maskKey = new byte[4];
                if (masked) ReadExact(stream, maskKey, 4);

                byte[] payload = new byte[payloadLen];
                ReadExact(stream, payload, payloadLen);

                if (masked)
                {
                    for (int i = 0; i < payloadLen; i++) payload[i] = (byte)(payload[i] ^ maskKey[i % 4]);
                }

                if (opcode == 1)
                {
                    MessageQueue.Enqueue((clientId, Encoding.UTF8.GetString(payload)));
                }
            }
        }
        catch (Exception) { }
        finally
        {
            clients.TryRemove(clientId, out _);
            LeaveQueue.Enqueue(clientId);
            client.Close();
        }
    }

    // 固定長のバイト列を確実に読み込む
    private void ReadExact(NetworkStream stream, byte[] buffer, int length)
    {
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    public void SendToClient(string clientId, string jsonMessage)
    {
        // 単一クライアントへWebSocketテキストフレームを送信
        if (clientId == "LOCAL" || !clients.TryGetValue(clientId, out TcpClient client) || !client.Connected) return;
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(jsonMessage);
            List<byte> frame = new List<byte> { 0x81 };
            if (payload.Length <= 125)
            {
                frame.Add((byte)payload.Length);
            }
            else
            {
                frame.Add(126);
                frame.Add((byte)((payload.Length >> 8) & 0xFF));
                frame.Add((byte)(payload.Length & 0xFF));
            }
            frame.AddRange(payload);
            byte[] frameBytes = frame.ToArray();

            lock (client)
            {
                NetworkStream stream = client.GetStream();
                stream.Write(frameBytes, 0, frameBytes.Length);
            }
        }
        catch (Exception) { }
    }

    // 接続中の全クライアントへ同報送信
    public void BroadcastToAll(string jsonMessage)
    {
        foreach (var id in clients.Keys) SendToClient(id, jsonMessage);
    }

    // WebSocketハンドシェイク用Acceptキーを計算
    private string ComputeWebSocketAcceptKey(string key)
    {
        string combined = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        byte[] hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    // ローカルIPv4アドレスを取得
    private string GetLocalIPAddress()
    {
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    // 全画面表示＆見切れ防止対応の横向き固定コントローラー(HTML)
    private string GetControllerHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover'>
<meta name='apple-mobile-web-app-capable' content='yes'>
<meta name='mobile-web-app-capable' content='yes'>
<title>Raid Controller</title>
<style>
  * {
    box-sizing: border-box;
    user-select: none;
    -webkit-user-select: none;
    -webkit-touch-callout: none;
  }
  html, body {
    margin: 0;
    padding: 0;
    width: 100%;
    height: 100%;
    background: #12141c;
    color: #fff;
    font-family: sans-serif;
    overflow: hidden;
    touch-action: none;
    transition: background 0.3s;
  }
  #app {
    position: fixed;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }
  #header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 6px 14px;
    background: rgba(0, 0, 0, 0.6);
    border-bottom: 3px solid #444;
    height: 42px;
    flex-shrink: 0;
  }
  #statusText {
    font-weight: bold;
    font-size: 15px;
    letter-spacing: 0.5px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  #fsBtn {
    background: rgba(255, 255, 255, 0.15);
    color: #fff;
    border: 1px solid rgba(255, 255, 255, 0.5);
    border-radius: 6px;
    padding: 4px 10px;
    font-size: 12px;
    font-weight: bold;
    flex-shrink: 0;
    margin-left: 10px;
  }
  #main {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: row;
    align-items: stretch;
    justify-content: space-between;
    padding: 10px 14px;
    gap: 14px;
    overflow: hidden;
  }
  .dpad-group {
    flex: 1.15;
    min-height: 0;
    display: flex;
    flex-direction: row;
    gap: 12px;
  }
  .action-group {
    flex: 0.85;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .ctrl-btn {
    flex: 1;
    min-height: 0;
    background: rgba(255, 255, 255, 0.1);
    border: 3px solid rgba(255, 255, 255, 0.4);
    border-radius: 16px;
    color: #fff;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    font-size: 24px;
    font-weight: 900;
    cursor: pointer;
    overflow: hidden;
  }
  .ctrl-btn span {
    font-size: 12px;
    font-weight: bold;
    opacity: 0.75;
    margin-top: 4px;
  }
  .ctrl-btn.active {
    background: rgba(255, 255, 255, 0.38);
    border-color: #fff;
    transform: scale(0.97);
  }
  #relicBtn {
    display: none;
    flex: 1;
    min-height: 0;
    background: linear-gradient(135deg, #ffcc00, #ff5500);
    color: #000;
    font-size: 22px;
    font-weight: 900;
    border: 3px solid #fff;
    border-radius: 16px;
    box-shadow: 0 0 20px rgba(255, 170, 0, 0.8);
  }
  #relicBtn.active {
    background: #fff;
    transform: scale(0.96);
  }
</style>
</head>
<body>
  <div id='app'>
    <div id='header'>
      <div id='statusText'>CONNECTING...</div>
      <button id='fsBtn'>FULLSCREEN</button>
    </div>
    <div id='main'>
      <div class='dpad-group'>
        <div id='leftBtn' class='ctrl-btn'>&lt; LEFT<span>MOVE</span></div>
        <div id='rightBtn' class='ctrl-btn'>RIGHT &gt;<span>MOVE</span></div>
      </div>
      <div class='action-group'>
        <div id='fireBtn' class='ctrl-btn'>FIRE<span>HOLD TO SHOOT</span></div>
        <div id='relicBtn' class='ctrl-btn'>FIRE RELIC!<span>TAP IN GOLD LANE</span></div>
      </div>
    </div>
  </div>
<script>
  const app = document.getElementById('app');
  const header = document.getElementById('header');
  const statusText = document.getElementById('statusText');
  const fsBtn = document.getElementById('fsBtn');
  const leftBtn = document.getElementById('leftBtn');
  const rightBtn = document.getElementById('rightBtn');
  const fireBtn = document.getElementById('fireBtn');
  const relicBtn = document.getElementById('relicBtn');

  // ブラウザの実際の表示領域に合わせて1ピクセルも見切れないようサイズと回転を計算
  function fitViewport() {
    const w = window.innerWidth;
    const h = window.innerHeight;
    if (h > w) {
      // 縦持ち時：90度回転して横向きレイアウトに固定
      app.style.width = h + 'px';
      app.style.height = w + 'px';
      app.style.top = (h - w) / 2 + 'px';
      app.style.left = (w - h) / 2 + 'px';
      app.style.transform = 'rotate(90deg)';
    } else {
      // 横持ち時：そのまま実表示サイズにフィット
      app.style.width = w + 'px';
      app.style.height = h + 'px';
      app.style.top = '0px';
      app.style.left = '0px';
      app.style.transform = 'none';
    }
  }
  window.addEventListener('resize', fitViewport);
  window.addEventListener('orientationchange', () => setTimeout(fitViewport, 100));
  fitViewport();

  // 全画面表示＆横向きロック要求
  function requestFullScreenAndLock() {
    const docEl = document.documentElement;
    const reqFs = docEl.requestFullscreen || docEl.webkitRequestFullscreen || docEl.msRequestFullscreen;
    if (reqFs && !document.fullscreenElement && !document.webkitFullscreenElement) {
      reqFs.call(docEl).then(() => {
        if (screen.orientation && screen.orientation.lock) {
          screen.orientation.lock('landscape').catch(() => {});
        }
      }).catch(() => {});
    }
    setTimeout(fitViewport, 150);
  }

  fsBtn.addEventListener('touchstart', (e) => { e.preventDefault(); requestFullScreenAndLock(); }, { passive: false });
  fsBtn.addEventListener('click', requestFullScreenAndLock);
  // 初回タッチ時にも自動で全画面化を試行
  document.body.addEventListener('touchstart', requestFullScreenAndLock, { once: true });

  const ws = new WebSocket('ws://' + location.host);
  let leftPressed = false;
  let rightPressed = false;
  let firing = 0;
  let relicAction = 0;

  ws.onopen = () => { statusText.innerText = 'CONNECTED - WATCH MAIN SCREEN'; };
  ws.onclose = () => {
    statusText.innerText = 'DISCONNECTED - RELOAD PAGE';
    document.body.style.background = '#300';
  };

  ws.onmessage = (e) => {
    const d = JSON.parse(e.data);
    if (d.color) {
      document.body.style.background = d.color;
      header.style.borderColor = d.color;
    }
    if (d.text) statusText.innerText = d.text;
    if (d.relic !== undefined) {
      relicBtn.style.display = d.relic ? 'flex' : 'none';
      fitViewport();
    }
    if (d.vibrate && navigator.vibrate) {
      navigator.vibrate(d.vibrate);
    }
  };

  function bindButton(el, onPress, onRelease) {
    const start = (e) => {
      e.preventDefault();
      el.classList.add('active');
      onPress();
    };
    const end = (e) => {
      e.preventDefault();
      el.classList.remove('active');
      if (onRelease) onRelease();
    };
    el.addEventListener('touchstart', start, { passive: false });
    el.addEventListener('touchend', end, { passive: false });
    el.addEventListener('touchcancel', end, { passive: false });

    el.addEventListener('mousedown', start);
    el.addEventListener('mouseup', end);
    el.addEventListener('mouseleave', end);
  }

  bindButton(leftBtn, () => { leftPressed = true; }, () => { leftPressed = false; });
  bindButton(rightBtn, () => { rightPressed = true; }, () => { rightPressed = false; });
  bindButton(fireBtn, () => { firing = 1; }, () => { firing = 0; });
  bindButton(relicBtn, () => { relicAction = 1; }, null);

  setInterval(() => {
    if (ws.readyState === 1) {
      let moveX = 0;
      if (leftPressed && !rightPressed) moveX = -1;
      else if (rightPressed && !leftPressed) moveX = 1;

      ws.send(JSON.stringify({ x: moveX, f: firing, r: relicAction }));
      relicAction = 0;
    }
  }, 50);
</script>
</body>
</html>";
    }
}