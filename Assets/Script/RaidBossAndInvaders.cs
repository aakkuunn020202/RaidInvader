using System.Collections.Generic;
using UnityEngine;

public enum RaidPhase { InvaderWave, GimmickShield, DpsPhase, Victory, Defeat }

public class InvaderSwarm : MonoBehaviour
{
    // 雑魚インベーダー群のシングルトン
    public static InvaderSwarm Instance { get; private set; }

    private class InvaderNode
    {
        public Transform trans;
        public SpriteRenderer sr;
        public Color baseColor;
        public float hp;
        public float flashTimer;
    }

    private readonly List<InvaderNode> invaders = new List<InvaderNode>(); // 生存中インベーダー一覧

    [Header("インベーダー挙動パラメータ")]
    [SerializeField] private float moveDir = 1f; // 現在の移動方向（1 or -1）
    [SerializeField] private float baseMoveSpeed = 1.8f; // 基本移動速度
    [SerializeField] private float baseFireCooldown = 1.2f; // 初期射撃クールダウン

    private float moveSpeed; // 実際に使う移動速度
    private float fireCooldown; // 次弾発射までの残り時間

    public int AliveCount => invaders.Count;

    // インベーダー群シングルトンを設定
    private void Awake()
    {
        Instance = this;
        moveSpeed = baseMoveSpeed;
        fireCooldown = baseFireCooldown;
    }

    public void SpawnWave(int rows, int cols, float speedMultiplier = 1f)
    {
        // 指定段数・列数でウェーブ生成
        ClearWave();
        moveSpeed = baseMoveSpeed * speedMultiplier;
        float startX = -(cols - 1) * 0.9f * 0.5f;
        float startY = 2.4f;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                GameObject go = new GameObject($"Invader_{r}_{c}");
                go.transform.SetParent(transform);
                Vector3 pos = new Vector3(startX + c * 1.15f, startY - r * 0.85f, 0f);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = RaidSpriteFactory.GetInvaderSprite();
                Color col = (r == 0) ? new Color(1f, 0.35f, 0.85f) : new Color(0.3f, 1f, 0.85f);
                sr.color = col;
                sr.sortingOrder = 5;

                invaders.Add(new InvaderNode { trans = go.transform, sr = sr, baseColor = col, hp = 20f });
            }
        }
    }

    // 現在のウェーブを全削除
    public void ClearWave()
    {
        foreach (var inv in invaders)
        {
            if (inv.trans != null) Destroy(inv.trans.gameObject);
        }
        invaders.Clear();
    }

    // インベーダー移動・侵攻・射撃を更新
    private void Update()
    {
        if (invaders.Count == 0) return;

        float minX = 99f, maxX = -99f, minY = 99f;
        foreach (var inv in invaders)
        {
            // 被弾時の白フラッシュ復元
            if (inv.flashTimer > 0f)
            {
                inv.flashTimer -= Time.deltaTime;
                inv.sr.color = (inv.flashTimer > 0f) ? Color.white : inv.baseColor;
            }
            inv.trans.localScale = Vector3.Lerp(inv.trans.localScale, new Vector3(0.9f, 0.9f, 1f), Time.deltaTime * 15f);

            Vector3 p = inv.trans.position;
            p.x += moveDir * moveSpeed * Time.deltaTime;
            inv.trans.position = p;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
        }

        if ((moveDir > 0f && maxX > 7.8f) || (moveDir < 0f && minX < -7.8f))
        {
            moveDir *= -1f;
            foreach (var inv in invaders) inv.trans.position += Vector3.down * 0.35f;
        }

        if (minY < -3.4f)
        {
            RaidGameManager.Instance.DamageCityShield(20f);
            RaidGameManager.Instance.SetAnnouncement("INVADERS BREACHED DEFENSE LINE!!", 2f);
            if (RaidJuiceManager.Instance != null) RaidJuiceManager.Instance.TriggerScreenFlash(Color.red, 0.5f);
            foreach (var inv in invaders) inv.trans.position += Vector3.up * 2.2f;
        }

        fireCooldown -= Time.deltaTime;
        if (fireCooldown <= 0f)
        {
            fireCooldown = Mathf.Max(0.35f, 1.1f - (24 - invaders.Count) * 0.03f);
            var shooter = invaders[Random.Range(0, invaders.Count)];
            RaidBullet.Spawn(shooter.trans.position + Vector3.down * 0.4f, Vector3.down * 6.5f, ElementColor.None, false, true, 10f, new Color(1f, 0.2f, 0.6f));
        }
    }

    public bool CheckBulletHit(RaidBullet b)
    {
        // プレイヤー弾との当たり判定
        for (int i = invaders.Count - 1; i >= 0; i--)
        {
            var inv = invaders[i];
            if (Vector2.Distance(b.transform.position, inv.trans.position) < 0.58f)
            {
                inv.hp -= b.Damage;
                inv.flashTimer = 0.07f;
                inv.sr.color = Color.white;
                inv.trans.localScale = new Vector3(1.25f, 0.65f, 1f);

                if (RaidJuiceManager.Instance != null)
                {
                    RaidJuiceManager.Instance.RegisterDamageHit(inv.trans.position, b.Damage, false);
                }

                if (inv.hp <= 0f)
                {
                    if (RaidJuiceManager.Instance != null)
                    {
                        RaidJuiceManager.Instance.SpawnExplosion(inv.trans.position, inv.baseColor, 20, 8.5f, 0.16f, true);
                    }
                    Destroy(inv.trans.gameObject);
                    invaders.RemoveAt(i);
                }
                else if (RaidJuiceManager.Instance != null)
                {
                    RaidJuiceManager.Instance.SpawnExplosion(b.transform.position, Color.white, 6, 4f, 0.04f, false);
                }
                return true;
            }
        }
        return false;
    }
}

public class RaidBoss : MonoBehaviour
{
    // レイドボス制御のシングルトン
    public static RaidBoss Instance { get; private set; }

    public RaidPhase CurrentPhase { get; private set; } = RaidPhase.InvaderWave; // 現在フェーズ

    [Header("ボス基本パラメータ")]
    [SerializeField] private float maxHp = 3500f; // ボス最大HP
    [SerializeField] private float gimmickPhaseDuration = 30f; // ギミックフェーズ制限時間
    [SerializeField] private float dpsPhaseDuration = 15f; // DPSフェーズ制限時間
    [SerializeField] private float coreMaxHp = 180f; // 各コアの最大HP

    public float MaxHp { get; private set; }
    public float CurrentHp { get; private set; }
    public float PhaseTimer { get; private set; } // 現フェーズの残り時間
    public int ActiveSyncLane { get; private set; } = -1; // 指定された同期レーン
    public bool WaitingForRelic { get; private set; } // レリック発射待ち状態

    private SpriteRenderer bossSr; // ボス本体スプライト
    private SpriteRenderer shieldRingSr; // シールド表示
    private Color bossBaseColor; // ボス通常色
    private float bossFlashTimer; // 被弾フラッシュ時間
    private Vector3 bossBaseScale = new Vector3(1.3f, 1.3f, 1f); // ボス通常スケール

    private Transform redCoreTrans, blueCoreTrans; // 赤青コアのTransform
    private SpriteRenderer redCoreSr, blueCoreSr; // 赤青コアの見た目
    public float RedCoreHp { get; private set; }
    public float BlueCoreHp { get; private set; }
    public float CoreMaxHp => coreMaxHp;

    private float bossFireTimer; // ボス弾発射タイマー

    // ボス初期化
    private void Awake()
    {
        Instance = this;
        MaxHp = maxHp;
        CurrentHp = MaxHp;
        BuildBossVisuals();
    }

    // ボス見た目とコアを構築
    private void BuildBossVisuals()
    {
        transform.position = new Vector3(0f, 3.8f, 0f);
        transform.localScale = bossBaseScale;

        bossSr = gameObject.AddComponent<SpriteRenderer>();
        bossSr.sprite = RaidSpriteFactory.GetBossSprite();
        bossSr.sortingOrder = 6;

        GameObject shieldObj = new GameObject("BossShieldBar");
        shieldObj.transform.SetParent(transform, false);
        shieldObj.transform.localPosition = new Vector3(0f, -0.95f, 0f);
        shieldObj.transform.localScale = new Vector3(3.8f, 0.15f, 1f);
        shieldRingSr = shieldObj.AddComponent<SpriteRenderer>();
        shieldRingSr.sprite = RaidSpriteFactory.GetWhiteSquare();
        shieldRingSr.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        shieldRingSr.sortingOrder = 7;

        redCoreTrans = CreateCore("RedCore", new Vector3(-4.8f, 2.5f, 0f), new Color(1f, 0.2f, 0.2f), out redCoreSr);
        blueCoreTrans = CreateCore("BlueCore", new Vector3(4.8f, 2.5f, 0f), new Color(0.2f, 0.6f, 1f), out blueCoreSr);
    }

    // コアを1つ生成する共通処理
    private Transform CreateCore(string name, Vector3 worldPos, Color col, out SpriteRenderer sr)
    {
        GameObject go = new GameObject(name);
        go.transform.position = worldPos;
        go.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RaidSpriteFactory.GetCoreSprite();
        sr.color = col;
        sr.sortingOrder = 6;
        go.SetActive(false);
        return go.transform;
    }

    public void StartWavePhase()
    {
        // フェーズ1：雑魚ウェーブ
        CurrentPhase = RaidPhase.InvaderWave;
        WaitingForRelic = false;
        ActiveSyncLane = -1;
        bossBaseColor = new Color(0.45f, 0.5f, 0.6f);
        bossSr.color = bossBaseColor;
        shieldRingSr.gameObject.SetActive(true);
        shieldRingSr.color = new Color(0.2f, 0.8f, 1f, 0.8f);

        redCoreTrans.gameObject.SetActive(false);
        blueCoreTrans.gameObject.SetActive(false);

        RaidGameManager.Instance.ResetAllPlayersState();
        InvaderSwarm.Instance.SpawnWave(3, 8, 1f);
        RaidGameManager.Instance.SetAnnouncement("PHASE 1: INTERCEPT INVADER SWARM!", 3.5f);
    }

    public void StartGimmickPhase()
    {
        // フェーズ2：色合わせギミック
        CurrentPhase = RaidPhase.GimmickShield;
        PhaseTimer = gimmickPhaseDuration;
        WaitingForRelic = false;
        ActiveSyncLane = -1;

        bossBaseColor = new Color(0.75f, 0.3f, 0.9f);
        bossSr.color = bossBaseColor;
        shieldRingSr.gameObject.SetActive(true);
        shieldRingSr.color = new Color(1f, 0.2f, 0.8f, 0.9f);

        RedCoreHp = CoreMaxHp;
        BlueCoreHp = CoreMaxHp;
        redCoreTrans.gameObject.SetActive(true);
        blueCoreTrans.gameObject.SetActive(true);

        if (RaidJuiceManager.Instance != null)
        {
            RaidJuiceManager.Instance.TriggerScreenFlash(new Color(0.8f, 0.2f, 1f), 0.35f);
            RaidJuiceManager.Instance.SpawnExplosion(redCoreTrans.position, Color.red, 16, 6f, 0.15f, true);
            RaidJuiceManager.Instance.SpawnExplosion(blueCoreTrans.position, Color.cyan, 16, 6f, 0.15f, true);
        }

        InvaderSwarm.Instance.SpawnWave(1, 6, 1.3f);
        RaidGameManager.Instance.AssignElementsToPlayers();
        RaidGameManager.Instance.SetAnnouncement("PHASE 2 [GIMMICK]: DESTROY MATCHING COLOR CORES!", 4f);
    }

    private void TriggerRelicStep()
    {
        // 両コア破壊後、レリック役と同期レーンを指定
        WaitingForRelic = true;
        ActiveSyncLane = Random.Range(0, 3);
        string[] laneNames = { "LEFT [ALPHA]", "CENTER [BETA]", "RIGHT [GAMMA]" };

        if (RaidJuiceManager.Instance != null)
        {
            RaidJuiceManager.Instance.TriggerScreenFlash(new Color(1f, 0.85f, 0.1f), 0.45f);
        }
        RaidGameManager.Instance.GrantRelicToRandomPlayer();
        RaidGameManager.Instance.SetAnnouncement($"CORES DOWN! RELIC HOLDER -> STAND IN {laneNames[ActiveSyncLane]} LANE & FIRE!", 5f);
    }

    public void StartDpsPhase()
    {
        // フェーズ3：ボス本体DPS時間
        CurrentPhase = RaidPhase.DpsPhase;
        PhaseTimer = dpsPhaseDuration;
        WaitingForRelic = false;
        ActiveSyncLane = -1;

        InvaderSwarm.Instance.ClearWave();
        redCoreTrans.gameObject.SetActive(false);
        blueCoreTrans.gameObject.SetActive(false);
        shieldRingSr.gameObject.SetActive(false);
        bossBaseColor = new Color(1f, 0.9f, 0.15f);
        bossSr.color = bossBaseColor;

        if (RaidJuiceManager.Instance != null)
        {
            RaidJuiceManager.Instance.SpawnExplosion(transform.position, new Color(1f, 0.85f, 0.1f), 55, 14f, 0.8f, true);
            RaidJuiceManager.Instance.RegisterDamageHit(transform.position, 999f, true, "SHIELD BROKEN!!");
        }

        RaidNetworkServer.Instance.BroadcastToAll("{\"color\":\"#776000\",\"text\":\"SHIELD BROKEN!! UNLEASH FULL DPS!!\",\"relic\":false,\"vibrate\":600}");
        RaidGameManager.Instance.SetAnnouncement("SHIELD BROKEN!! FEVER DPS MODE (3-WAY SHOT UNLOCKED)!!", 4f);
    }

    private void ExecuteWipe()
    {
        // ギミック失敗時の全体ペナルティ
        RaidGameManager.Instance.DamageCityShield(35f);
        if (RaidJuiceManager.Instance != null)
        {
            RaidJuiceManager.Instance.TriggerScreenFlash(Color.red, 0.85f);
            RaidJuiceManager.Instance.SpawnExplosion(Vector3.zero, Color.red, 60, 16f, 0.9f, true);
        }
        RaidNetworkServer.Instance.BroadcastToAll("{\"color\":\"#990000\",\"text\":\"WIPE MECHANIC TRIGGERED! SHIELD FAILED!\",\"relic\":false,\"vibrate\":900}");
        RaidGameManager.Instance.SetAnnouncement("OMEGA WIPE TRIGGERED!! MASSIVE DAMAGE TO CITY SHIELD!", 4f);

        if (RaidGameManager.Instance.CityShield > 0f)
        {
            StartWavePhase();
        }
    }

    // フェーズ進行・ボス攻撃・演出更新
    private void Update()
    {
        if (CurrentPhase == RaidPhase.Victory || CurrentPhase == RaidPhase.Defeat) return;

        transform.position = new Vector3(Mathf.Sin(Time.time * 1.1f) * 2.2f, 3.8f, 0f);
        transform.localScale = Vector3.Lerp(transform.localScale, bossBaseScale, Time.deltaTime * 14f);

        if (bossFlashTimer > 0f)
        {
            bossFlashTimer -= Time.deltaTime;
            bossSr.color = (bossFlashTimer > 0f) ? Color.white : bossBaseColor;
        }

        redCoreTrans.localScale = Vector3.Lerp(redCoreTrans.localScale, new Vector3(1.5f, 1.5f, 1f), Time.deltaTime * 12f);
        blueCoreTrans.localScale = Vector3.Lerp(blueCoreTrans.localScale, new Vector3(1.5f, 1.5f, 1f), Time.deltaTime * 12f);

        bossFireTimer -= Time.deltaTime;
        if (bossFireTimer <= 0f)
        {
            bossFireTimer = (CurrentPhase == RaidPhase.DpsPhase) ? 1.8f : 1.1f;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 dir = new Vector3(i * 2.2f, -7f, 0f);
                RaidBullet.Spawn(transform.position + Vector3.down * 0.6f, dir, ElementColor.None, false, true, 10f, new Color(1f, 0.5f, 0f));
            }
        }

        if (CurrentPhase == RaidPhase.InvaderWave)
        {
            if (InvaderSwarm.Instance.AliveCount == 0) StartGimmickPhase();
        }
        else if (CurrentPhase == RaidPhase.GimmickShield)
        {
            PhaseTimer -= Time.deltaTime;
            if (PhaseTimer <= 0f)
            {
                ExecuteWipe();
                return;
            }

            if (!WaitingForRelic && RedCoreHp <= 0f && BlueCoreHp <= 0f)
            {
                TriggerRelicStep();
            }
        }
        else if (CurrentPhase == RaidPhase.DpsPhase)
        {
            PhaseTimer -= Time.deltaTime;
            if (PhaseTimer <= 0f) StartWavePhase();
        }
    }

    public bool IsPositionInActiveLane(float x)
    {
        // 指定X座標が現在の同期レーン内か判定
        if (ActiveSyncLane == 0) return x < -2.8f;
        if (ActiveSyncLane == 1) return x >= -2.8f && x <= 2.8f;
        if (ActiveSyncLane == 2) return x > 2.8f;
        return true;
    }

    public bool CheckBulletHit(RaidBullet b)
    {
        // コア・ボス本体に対する弾ヒット判定
        if (CurrentPhase == RaidPhase.GimmickShield)
        {
            if (RedCoreHp > 0f && Vector2.Distance(b.transform.position, redCoreTrans.position) < 0.95f)
            {
                if (b.Element == ElementColor.Red)
                {
                    RedCoreHp -= b.Damage;
                    redCoreTrans.localScale = new Vector3(1.9f, 1.9f, 1f);
                    if (RaidJuiceManager.Instance != null)
                    {
                        RaidJuiceManager.Instance.RegisterDamageHit(redCoreTrans.position, b.Damage, true);
                        RaidJuiceManager.Instance.SpawnExplosion(b.transform.position, Color.red, 10, 6f, 0.1f, false);
                    }
                    if (RedCoreHp <= 0f)
                    {
                        if (RaidJuiceManager.Instance != null)
                        {
                            RaidJuiceManager.Instance.SpawnExplosion(redCoreTrans.position, Color.red, 35, 11f, 0.4f, true);
                            RaidJuiceManager.Instance.TriggerHitStop(0.08f);
                        }
                        redCoreTrans.gameObject.SetActive(false);
                        RaidGameManager.Instance.CheckSoloPlayerElementSwitch(ElementColor.Blue);
                    }
                }
                else if (RaidJuiceManager.Instance != null)
                {
                    RaidJuiceManager.Instance.RegisterDamageHit(redCoreTrans.position, 0f, false, "IMMUNE (NEED RED)");
                }
                return true;
            }

            if (BlueCoreHp > 0f && Vector2.Distance(b.transform.position, blueCoreTrans.position) < 0.95f)
            {
                if (b.Element == ElementColor.Blue)
                {
                    BlueCoreHp -= b.Damage;
                    blueCoreTrans.localScale = new Vector3(1.9f, 1.9f, 1f);
                    if (RaidJuiceManager.Instance != null)
                    {
                        RaidJuiceManager.Instance.RegisterDamageHit(blueCoreTrans.position, b.Damage, true);
                        RaidJuiceManager.Instance.SpawnExplosion(b.transform.position, Color.cyan, 10, 6f, 0.1f, false);
                    }
                    if (BlueCoreHp <= 0f)
                    {
                        if (RaidJuiceManager.Instance != null)
                        {
                            RaidJuiceManager.Instance.SpawnExplosion(blueCoreTrans.position, Color.cyan, 35, 11f, 0.4f, true);
                            RaidJuiceManager.Instance.TriggerHitStop(0.08f);
                        }
                        blueCoreTrans.gameObject.SetActive(false);
                        RaidGameManager.Instance.CheckSoloPlayerElementSwitch(ElementColor.Red);
                    }
                }
                else if (RaidJuiceManager.Instance != null)
                {
                    RaidJuiceManager.Instance.RegisterDamageHit(blueCoreTrans.position, 0f, false, "IMMUNE (NEED BLUE)");
                }
                return true;
            }
        }

        if (Mathf.Abs(b.transform.position.x - transform.position.x) < 2.0f &&
            Mathf.Abs(b.transform.position.y - transform.position.y) < 1.0f)
        {
            if (CurrentPhase == RaidPhase.GimmickShield && WaitingForRelic && b.IsRelic)
            {
                StartDpsPhase();
                return true;
            }
            else if (CurrentPhase == RaidPhase.DpsPhase)
            {
                CurrentHp = Mathf.Max(0f, CurrentHp - b.Damage);
                bossFlashTimer = 0.06f;
                bossSr.color = Color.white;
                transform.localScale = new Vector3(1.48f, 1.12f, 1f);

                if (RaidJuiceManager.Instance != null)
                {
                    RaidJuiceManager.Instance.RegisterDamageHit(b.transform.position, b.Damage, true);
                    RaidJuiceManager.Instance.SpawnExplosion(b.transform.position, new Color(1f, 0.85f, 0.1f), 12, 8f, 0.14f, false);
                }

                if (CurrentHp <= 0f)
                {
                    CurrentPhase = RaidPhase.Victory;
                    InvaderSwarm.Instance.ClearWave();
                    if (RaidJuiceManager.Instance != null)
                    {
                        RaidJuiceManager.Instance.TriggerScreenFlash(Color.white, 0.95f);
                        for (int i = 0; i < 6; i++)
                        {
                            Vector3 rp = transform.position + (Vector3)Random.insideUnitCircle * 2.5f;
                            RaidJuiceManager.Instance.SpawnExplosion(rp, Color.HSVToRGB(i / 6f, 0.8f, 1f), 45, 15f, 1.0f, true);
                        }
                    }
                    RaidNetworkServer.Instance.BroadcastToAll("{\"color\":\"#006622\",\"text\":\"RAID BOSS DEFEATED!! VICTORY!!\",\"vibrate\":1000}");
                    RaidGameManager.Instance.SetAnnouncement("RAID COMPLETE!! OMEGA INVADER DESTROYED!!", 999f);
                    gameObject.SetActive(false);
                }
                return true;
            }
            else if (RaidJuiceManager.Instance != null && Random.value < 0.35f)
            {
                RaidJuiceManager.Instance.SpawnExplosion(b.transform.position, Color.cyan, 4, 3f, 0.02f, false);
                RaidJuiceManager.Instance.RegisterDamageHit(b.transform.position, 0f, false, "SHIELDED");
            }
            return true;
        }
        return false;
    }

    // 都市防衛失敗時に敗北状態へ遷移
    public void SetDefeat()
    {
        CurrentPhase = RaidPhase.Defeat;
        InvaderSwarm.Instance.ClearWave();
    }

    // RaidGameManagerからボス調整値を適用する
    public void ConfigureBossParameters(
        float configuredMaxHp,
        float configuredGimmickPhaseDuration,
        float configuredDpsPhaseDuration,
        float configuredCoreMaxHp)
    {
        maxHp = configuredMaxHp;
        gimmickPhaseDuration = configuredGimmickPhaseDuration;
        dpsPhaseDuration = configuredDpsPhaseDuration;
        coreMaxHp = configuredCoreMaxHp;

        MaxHp = maxHp;
        CurrentHp = maxHp;
    }
}