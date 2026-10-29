using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class RaidGameManager : MonoBehaviour
{
    // ゲーム全体から参照するシングルトン
    public static RaidGameManager Instance { get; private set; }

    [Header("ゲーム進行パラメータ")]
    [SerializeField] private float initialCityShield = 100000f; // 都市シールド初期値
    [SerializeField] private float soloPolarityAnnouncementDuration = 2.5f; // ソロ属性切替メッセージ表示時間
    [SerializeField] private float raidFailedAnnouncementDuration = 999f; // 失敗時メッセージ表示時間

    [Header("プレイヤー出現位置パラメータ")]
    [SerializeField] private float joinSpawnXMin = -4.5f; // 通常参加プレイヤーの最小X
    [SerializeField] private float joinSpawnXMax = 4.5f; // 通常参加プレイヤーの最大X
    [SerializeField] private float testSpawnXMin = -4.0f; // テスト参加プレイヤーの最小X
    [SerializeField] private float testSpawnXMax = 4.0f; // テスト参加プレイヤーの最大X
    [SerializeField] private float playerSpawnY = -4.4f; // プレイヤー出現Y座標

    [Header("プレイヤー戦闘パラメータ")]
    [SerializeField] private float playerMoveSpeed = 9.5f; // プレイヤー移動速度
    [SerializeField] private float playerXLimit = 8.2f; // プレイヤー横移動可能範囲
    [SerializeField] private float playerBaseFireInterval = 0.22f; // プレイヤー通常連射間隔
    [SerializeField] private float playerNormalShotDamage = 14f; // 通常ショットダメージ
    [SerializeField] private float playerDpsShotDamage = 25f; // DPS中央ショットダメージ
    [SerializeField] private float playerDpsSideDamageRate = 0.6f; // DPS左右弾ダメージ倍率
    [SerializeField] private float playerShotSpeedY = 16f; // 通常ショット速度
    [SerializeField] private float playerDpsSideShotSpeedX = 2.4f; // DPS左右弾X速度
    [SerializeField] private float playerDpsSideShotSpeedY = 15.5f; // DPS左右弾Y速度
    [SerializeField] private float playerRelicShotSpeedY = 22f; // レリックショット速度
    [SerializeField] private float playerRelicShotDamage = 150f; // レリックショットダメージ
    [SerializeField] private float playerHitStunDuration = 1.3f; // 被弾時硬直時間
    [SerializeField] private float playerHitCityShieldDamage = 5f; // 被弾時に都市シールドへ与えるダメージ

    [Header("ボス戦闘パラメータ")]
    [SerializeField] private float bossMaxHp = 3500f; // ボス最大HP
    [SerializeField] private float bossGimmickPhaseDuration = 30f; // ギミックフェーズ時間
    [SerializeField] private float bossDpsPhaseDuration = 15f; // DPSフェーズ時間
    [SerializeField] private float bossCoreMaxHp = 180f; // 赤青コア最大HP

    // 都市シールドの残量
    public float CityShield { get; private set; } = 100000f; //元残量は100f
    // 接続中プレイヤーをIDで管理
    private readonly Dictionary<string, PlayerShip> activePlayers = new Dictionary<string, PlayerShip>();

    private string announcementText = ""; // 画面中央の告知テキスト
    private float announcementTimer; // 告知表示の残り時間
    private SpriteRenderer laneHighlightSr; // 同期レーン強調表示

    private Texture2D qrCodeTexture; // 接続用QRコード
    private string lastGeneratedUrl = ""; // QR再生成判定用URL
    private int dummyPlayerCount = 0; // テスト参加者の採番カウンタ

    [Header("カメラ・フィールド設定")]
    [SerializeField] private float cameraOrthoSize = 5.6f; // メインカメラのサイズ
    [SerializeField] private Color cameraBackgroundColor = new Color(0.03f, 0.04f, 0.08f, 1f); // 背景色
    [SerializeField] private Vector3 defenseLinePosition = new Vector3(0f, -3.6f, 0f); // 防衛ライン位置
    [SerializeField] private Vector3 defenseLineScale = new Vector3(18f, 0.04f, 1f); // 防衛ラインサイズ
    [SerializeField] private Color defenseLineColor = new Color(0.2f, 0.9f, 0.5f, 0.35f); // 防衛ライン色
    [SerializeField] private Vector3 syncLanePosition = new Vector3(0f, -1f, 0f); // 同期レーン表示位置
    [SerializeField] private Vector3 syncLaneScale = new Vector3(5.4f, 9f, 1f); // 同期レーン表示サイズ
    [SerializeField] private Color syncLaneColor = new Color(1f, 0.85f, 0.1f, 0.18f); // 同期レーン色
    [SerializeField] private int syncLaneSortingOrder = 1; // 同期レーンの描画順
    [SerializeField] private float leftLaneX = -5.6f; // 左レーン中心X
    [SerializeField] private float centerLaneX = 0f; // 中央レーン中心X
    [SerializeField] private float rightLaneX = 5.6f; // 右レーン中心X

    [Header("UI表示パラメータ")]
    [SerializeField] private float bossHpBarWidth = 520f; // ボスHPバー横幅
    [SerializeField] private float announcementWidth = 740f; // アナウンス枠横幅
    [SerializeField] private float announcementY = 90f; // アナウンス枠Y座標
    [SerializeField] private float announcementHeight = 44f; // アナウンス枠高さ

    [System.Serializable]
    private struct InputPacket
    {
        // 横移動入力（-1?1）
        public float x;
        // 射撃入力（1で押下）
        public int f;
        // レリック発射入力（1で押下）
        public int r;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        CityShield = initialCityShield;
    }

    // 初期シーン構築（カメラ、演出、敵生成）
    private void Start()
    {
        SetupCameraAndEnvironment();
        RaidJuiceManager.EnsureInstance(); // 演出マネージャーを自動生成

        GameObject swarmObj = new GameObject("InvaderSwarm");
        swarmObj.AddComponent<InvaderSwarm>();

        GameObject bossObj = new GameObject("RaidBoss");
        RaidBoss boss = bossObj.AddComponent<RaidBoss>();
        ApplyBossParameters(boss);
        boss.StartWavePhase();
    }

    // カメラと背景オブジェクトを初期化
    private void SetupCameraAndEnvironment()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = cameraOrthoSize;
            cam.backgroundColor = cameraBackgroundColor;
        }

        GameObject lineObj = new GameObject("DefenseLine");
        lineObj.transform.position = defenseLinePosition;
        lineObj.transform.localScale = defenseLineScale;
        SpriteRenderer lineSr = lineObj.AddComponent<SpriteRenderer>();
        lineSr.sprite = RaidSpriteFactory.GetWhiteSquare();
        lineSr.color = defenseLineColor;

        GameObject laneObj = new GameObject("SyncLaneHighlight");
        laneObj.transform.position = syncLanePosition;
        laneObj.transform.localScale = syncLaneScale;
        laneHighlightSr = laneObj.AddComponent<SpriteRenderer>();
        laneHighlightSr.sprite = RaidSpriteFactory.GetWhiteSquare();
        laneHighlightSr.color = syncLaneColor;
        laneHighlightSr.sortingOrder = syncLaneSortingOrder;
        laneObj.SetActive(false);
    }

    // 毎フレームのゲーム進行管理
    private void Update()
    {
        // アナウンス表示時間の更新
        if (announcementTimer > 0f) announcementTimer -= Time.deltaTime;

        var server = RaidNetworkServer.Instance;
        if (server != null && !string.IsNullOrEmpty(server.ServerUrl) && server.ServerUrl != lastGeneratedUrl)
        {
            lastGeneratedUrl = server.ServerUrl;
            qrCodeTexture = RaidSpriteFactory.GenerateQrCodeTexture(lastGeneratedUrl);
        }

        if (WasSpacePressedThisFrame() && !activePlayers.ContainsKey("LOCAL"))
        {
            SpawnTestPlayer("LOCAL", true);
        }

        if (server != null)
        {
            // 新規参加プレイヤーの生成
            while (server.JoinQueue.TryDequeue(out string newId))
            {
                Vector3 pos = new Vector3(Random.Range(joinSpawnXMin, joinSpawnXMax), playerSpawnY, 0f);
                PlayerShip ship = PlayerShip.Create(newId, pos, false);
                ApplyPlayerParameters(ship);
                activePlayers[newId] = ship;

                if (RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.GimmickShield)
                {
                    AssignElementsToPlayers();
                }
            }

            // 切断プレイヤーの後始末
            while (server.LeaveQueue.TryDequeue(out string leaveId))
            {
                if (activePlayers.TryGetValue(leaveId, out PlayerShip ship))
                {
                    bool hadRelic = ship.HasRelic;
                    if (ship != null) Destroy(ship.gameObject);
                    activePlayers.Remove(leaveId);
                    if (hadRelic && RaidBoss.Instance != null && RaidBoss.Instance.WaitingForRelic)
                    {
                        GrantRelicToRandomPlayer();
                    }
                }
            }

            // 受信入力を各プレイヤーに適用
            while (server.MessageQueue.TryDequeue(out var msg))
            {
                if (activePlayers.TryGetValue(msg.id, out PlayerShip ship))
                {
                    InputPacket packet = JsonUtility.FromJson<InputPacket>(msg.json);
                    ship.ApplyInput(packet.x, packet.f, packet.r);
                }
            }
        }

        if (RaidBoss.Instance != null && RaidBoss.Instance.WaitingForRelic && RaidBoss.Instance.ActiveSyncLane >= 0)
        {
            laneHighlightSr.gameObject.SetActive(true);
            float laneX = centerLaneX;
            if (RaidBoss.Instance.ActiveSyncLane == 0) laneX = leftLaneX;
            else if (RaidBoss.Instance.ActiveSyncLane == 2) laneX = rightLaneX;
            laneHighlightSr.transform.position = new Vector3(laneX, syncLanePosition.y, syncLanePosition.z);
        }
        else
        {
            laneHighlightSr.gameObject.SetActive(false);
        }
    }

    // テスト/接続用プレイヤーを1体追加
    public void SpawnTestPlayer(string id, bool controllableByKeyboard)
    {
        if (activePlayers.ContainsKey(id)) return;
        Vector3 pos = new Vector3(Random.Range(testSpawnXMin, testSpawnXMax), playerSpawnY, 0f);
        PlayerShip ship = PlayerShip.Create(id, pos, controllableByKeyboard);
        ApplyPlayerParameters(ship);
        activePlayers[id] = ship;

        if (RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.GimmickShield)
        {
            AssignElementsToPlayers();
        }
    }

    // スペースキー押下を抽象化して取得
    private bool WasSpacePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.spaceKey.wasPressedThisFrame;
#endif
        try { return Input.GetKeyDown(KeyCode.Space); }
        catch { return false; }
    }

    // 参加中プレイヤー一覧を取得
    public IEnumerable<PlayerShip> GetAllPlayers() => activePlayers.Values;

    // 全プレイヤーの属性/レリック状態を初期化
    public void ResetAllPlayersState()
    {
        foreach (var p in activePlayers.Values)
        {
            p.SetRelicState(false);
            p.SetElement(ElementColor.None);
        }
    }

    // 参加プレイヤーへ赤青属性を割り当て
    public void AssignElementsToPlayers()
    {
        int i = 0;
        foreach (var p in activePlayers.Values)
        {
            p.SetRelicState(false);
            ElementColor elem = (i % 2 == 0) ? ElementColor.Red : ElementColor.Blue;
            if (RaidBoss.Instance.RedCoreHp <= 0f) elem = ElementColor.Blue;
            else if (RaidBoss.Instance.BlueCoreHp <= 0f) elem = ElementColor.Red;

            p.SetElement(elem);
            i++;
        }
    }

    // ソロ時の属性切替補助
    public void CheckSoloPlayerElementSwitch(ElementColor targetColor)
    {
        if (activePlayers.Count == 1)
        {
            foreach (var p in activePlayers.Values) p.SetElement(targetColor);
            SetAnnouncement($"SOLO POLARITY SHIFT: SWITCHED TO {targetColor}!", soloPolarityAnnouncementDuration);
        }
    }

    // ランダムな1人にレリックを付与
    public void GrantRelicToRandomPlayer()
    {
        if (activePlayers.Count == 0) return;
        List<PlayerShip> list = new List<PlayerShip>(activePlayers.Values);
        foreach (var p in list) p.SetRelicState(false);

        PlayerShip chosen = list[Random.Range(0, list.Count)];
        chosen.SetRelicState(true);
    }

    // 都市シールドへダメージを適用
    public void DamageCityShield(float dmg)
    {
        if (RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.Victory) return;
        CityShield = Mathf.Max(0f, CityShield - dmg);
        if (CityShield <= 0f && RaidBoss.Instance != null)
        {
            RaidBoss.Instance.SetDefeat();
            SetAnnouncement("CITY SHIELD DEPLETED... RAID FAILED!", raidFailedAnnouncementDuration);
        }
    }

    // 画面中央のアナウンスを設定
    public void SetAnnouncement(string text, float duration)
    {
        announcementText = text;
        announcementTimer = duration;
    }

    // 1体のプレイヤーへインスペクター設定値を適用
    private void ApplyPlayerParameters(PlayerShip ship)
    {
        if (ship == null) return;
        ship.ConfigureCombatParameters(
            playerMoveSpeed,
            playerXLimit,
            playerBaseFireInterval,
            playerNormalShotDamage,
            playerDpsShotDamage,
            playerDpsSideDamageRate,
            playerShotSpeedY,
            playerDpsSideShotSpeedX,
            playerDpsSideShotSpeedY,
            playerRelicShotSpeedY,
            playerRelicShotDamage,
            playerHitStunDuration,
            playerHitCityShieldDamage);
    }

    // ボスへインスペクター設定値を適用
    private void ApplyBossParameters(RaidBoss boss)
    {
        if (boss == null) return;
        boss.ConfigureBossParameters(
            bossMaxHp,
            bossGimmickPhaseDuration,
            bossDpsPhaseDuration,
            bossCoreMaxHp);
    }

    // 画面UI（QR、参加者数、ボスHP、アナウンス）を描画
    private void OnGUI()
    {
        // 左上：接続用QRと参加情報
        GUIStyle boldStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        boldStyle.normal.textColor = Color.white;

        GUI.Box(new Rect(15, 15, 230, 310), "");
        GUI.Label(new Rect(25, 22, 210, 22), "スマホでQRを読んで参加:", boldStyle);

        if (qrCodeTexture != null)
        {
            GUI.DrawTexture(new Rect(40, 48, 180, 180), qrCodeTexture, ScaleMode.ScaleToFit, false);
        }

        string url = RaidNetworkServer.Instance != null ? RaidNetworkServer.Instance.ServerUrl : "Starting...";
        GUI.Label(new Rect(25, 232, 210, 20), url, boldStyle);
        GUI.Label(new Rect(25, 252, 210, 20), $"参加中プレイヤー: {activePlayers.Count} 人", boldStyle);

        if (GUI.Button(new Rect(25, 278, 210, 36), "＋ 仮で参加 (白い四角形を追加)"))
        {
            if (!activePlayers.ContainsKey("LOCAL"))
            {
                SpawnTestPlayer("LOCAL", true);
            }
            else
            {
                dummyPlayerCount++;
                SpawnTestPlayer($"TEST-{dummyPlayerCount}", false);
            }
        }

        if (RaidBoss.Instance == null) return;

        // 上部中央：ボスHPバー
        float barW = bossHpBarWidth;
        float barX = (Screen.width - barW) * 0.5f;
        GUI.Box(new Rect(barX, 15, barW, 34), "");
        float hpRatio = RaidBoss.Instance.CurrentHp / RaidBoss.Instance.MaxHp;
        Color prev = GUI.color;
        GUI.color = (RaidBoss.Instance.CurrentPhase == RaidPhase.DpsPhase) ? new Color(1f, 0.85f, 0.1f) : new Color(0.85f, 0.15f, 0.3f);
        GUI.DrawTexture(new Rect(barX + 4, 19, (barW - 8) * hpRatio, 26), Texture2D.whiteTexture);
        GUI.color = prev;

        GUIStyle centerBold = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
        centerBold.normal.textColor = Color.white;
        GUI.Label(new Rect(barX, 15, barW, 34), $"OMEGA INVADER  [{RaidBoss.Instance.CurrentHp:F0} / {RaidBoss.Instance.MaxHp:F0}]  -  {RaidBoss.Instance.CurrentPhase}", centerBold);

        if (RaidBoss.Instance.CurrentPhase == RaidPhase.GimmickShield)
        {
            centerBold.fontSize = 22;
            centerBold.normal.textColor = new Color(1f, 0.35f, 0.35f);
            GUI.Label(new Rect(barX, 52, barW, 30), $"WIPE COUNTDOWN: {RaidBoss.Instance.PhaseTimer:F1}s", centerBold);
        }
        else if (RaidBoss.Instance.CurrentPhase == RaidPhase.DpsPhase)
        {
            centerBold.fontSize = 22;
            centerBold.normal.textColor = new Color(1f, 0.9f, 0.2f);
            GUI.Label(new Rect(barX, 52, barW, 30), $"FEVER DPS WINDOW: {RaidBoss.Instance.PhaseTimer:F1}s", centerBold);
        }

        if (announcementTimer > 0f && !string.IsNullOrEmpty(announcementText))
        {
            // 中央：フェーズ進行などのアナウンス
            GUIStyle announceStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
            announceStyle.normal.textColor = new Color(1f, 0.95f, 0.4f);
            GUI.Box(new Rect((Screen.width - announcementWidth) * 0.5f, announcementY, announcementWidth, announcementHeight), announcementText, announceStyle);
        }
    }
}