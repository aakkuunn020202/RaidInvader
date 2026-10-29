using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ElementColor { None, Red, Blue }

public class PlayerShip : MonoBehaviour
{
    // 接続元クライアントID
    public string ClientId { get; private set; }
    // 現在の属性（赤/青/なし）
    public ElementColor CurrentElement { get; private set; } = ElementColor.None;
    // レリック担当フラグ
    public bool HasRelic { get; private set; }
    public bool IsLocalDebug { get; set; }

    [Header("移動・攻撃パラメータ")]
    [SerializeField] private float moveSpeed = 9.5f; // プレイヤーの横移動速度
    [SerializeField] private float xLimit = 8.2f; // 横移動可能な最大X座標
    [SerializeField] private float baseFireInterval = 0.22f; // 通常時の連射間隔
    [SerializeField] private float normalShotDamage = 14f; // 通常時の弾ダメージ
    [SerializeField] private float dpsShotDamage = 25f; // DPS時の中央弾ダメージ
    [SerializeField] private float dpsSideDamageRate = 0.6f; // DPS時の左右弾ダメージ倍率
    [SerializeField] private float shotSpeedY = 16f; // 通常弾のY方向速度
    [SerializeField] private float dpsSideShotSpeedX = 2.4f; // DPS左右弾のX方向速度
    [SerializeField] private float dpsSideShotSpeedY = 15.5f; // DPS左右弾のY方向速度
    [SerializeField] private float relicShotSpeedY = 22f; // レリック弾のY方向速度
    [SerializeField] private float relicShotDamage = 150f; // レリック弾のダメージ
    [SerializeField] private float hitStunDuration = 1.3f; // 被弾時の硬直時間
    [SerializeField] private float hitCityShieldDamage = 5f; // 被弾時に都市シールドへ与えるダメージ

    private float inputX; // 現在フレームの移動入力値
    private bool isFiring; // 射撃入力中かどうか
    private bool triggerRelic; // レリック発動要求フラグ
    private float fireTimer; // 次弾発射までの待機タイマー
    private float stunTimer; // 被弾後の硬直タイマー

    private SpriteRenderer sr; // 機体表示用スプライト
    private TextMesh labelMesh; // プレイヤーID表示ラベル
    private Vector3 baseScale = new Vector3(0.75f, 0.75f, 1f); // 通常時スケール

    // 指定IDのプレイヤー機体を生成して返す
    public static PlayerShip Create(string id, Vector3 pos, bool isLocal = false)
    {
        // プレイヤー本体オブジェクトを動的生成
        GameObject go = new GameObject($"Player_{id}");
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.75f, 0.75f, 1f);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = RaidSpriteFactory.GetWhiteSquare();
        renderer.color = Color.white;
        renderer.sortingOrder = 10;

        PlayerShip ship = go.AddComponent<PlayerShip>();
        ship.sr = renderer;
        ship.ClientId = id;
        ship.IsLocalDebug = isLocal;

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(go.transform, false);
        labelObj.transform.localPosition = new Vector3(0f, -0.85f, 0f);
        labelObj.transform.localScale = new Vector3(0.14f, 0.14f, 1f);
        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = id;
        tm.anchor = TextAnchor.UpperCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 42;
        tm.color = Color.white;
        ship.labelMesh = tm;

        ship.SetElement(ElementColor.None);
        if (RaidJuiceManager.Instance != null)
        {
            RaidJuiceManager.Instance.SpawnExplosion(pos, Color.cyan, 18, 6f, 0.1f, true);
        }
        return ship;
    }

    // ネットワーク入力を機体制御用の状態に反映
    public void ApplyInput(float x, int fire, int relic)
    {
        inputX = x;
        isFiring = (fire == 1);
        if (relic == 1) triggerRelic = true;
    }

    // 毎フレームの移動・射撃・レリック発動を処理
    private void Update()
    {
        if (IsLocalDebug) ReadLocalKeyboardInput();

        // 発射反動のぷるぷるスケールアニメーション復元
        transform.localScale = Vector3.Lerp(transform.localScale, baseScale, Time.deltaTime * 18f);

        if (stunTimer > 0f)
        {
            stunTimer -= Time.deltaTime;
            sr.enabled = (Mathf.FloorToInt(stunTimer * 15f) % 2 == 0);
            return;
        }
        sr.enabled = true;

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x + inputX * moveSpeed * Time.deltaTime, -xLimit, xLimit);
        transform.position = pos;

        // 移動時のジェット噴射パーティクル
        if (Mathf.Abs(inputX) > 0.1f && RaidJuiceManager.Instance != null && Random.value < 0.45f)
        {
            RaidJuiceManager.Instance.SpawnTrail(transform.position + Vector3.down * 0.4f, sr.color, 0.18f);
        }

        bool isDps = RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.DpsPhase;
        float currentInterval = isDps ? baseFireInterval * 0.38f : baseFireInterval;

        fireTimer -= Time.deltaTime;
        if (isFiring && fireTimer <= 0f)
        {
            fireTimer = currentInterval;
            // 発射時の反動（Squash & Stretch）
            transform.localScale = new Vector3(0.95f, 0.55f, 1f);

            Color bulletCol = isDps ? new Color(1f, 0.92f, 0.15f) : sr.color;
            float dmg = isDps ? dpsShotDamage : normalShotDamage;
            Vector3 muzzle = transform.position + Vector3.up * 0.55f;

            if (RaidJuiceManager.Instance != null)
            {
                RaidJuiceManager.Instance.PlayShootSound();
                RaidJuiceManager.Instance.SpawnTrail(muzzle, Color.white, 0.32f);
            }

            // DPSフェーズ中は気持ちいい3WAY拡散レーザー化！
            RaidBullet.Spawn(muzzle, new Vector3(0f, shotSpeedY, 0f), CurrentElement, false, false, dmg, bulletCol);
            if (isDps)
            {
                RaidBullet.Spawn(muzzle, new Vector3(-dpsSideShotSpeedX, dpsSideShotSpeedY, 0f), CurrentElement, false, false, dmg * dpsSideDamageRate, new Color(1f, 0.5f, 0.1f));
                RaidBullet.Spawn(muzzle, new Vector3(dpsSideShotSpeedX, dpsSideShotSpeedY, 0f), CurrentElement, false, false, dmg * dpsSideDamageRate, new Color(1f, 0.5f, 0.1f));
            }
        }

        // レリック極太レーザー発射処理
        if (triggerRelic)
        {
            triggerRelic = false;
            if (HasRelic && RaidBoss.Instance != null)
            {
                if (RaidBoss.Instance.IsPositionInActiveLane(transform.position.x))
                {
                    SetRelicState(false);
                    if (RaidJuiceManager.Instance != null)
                    {
                        RaidJuiceManager.Instance.SpawnMegaLaserBeam(transform.position.x, new Color(1f, 0.85f, 0.1f));
                    }
                    RaidBullet.Spawn(transform.position + Vector3.up * 0.8f, Vector3.up * relicShotSpeedY, ElementColor.None, true, false, relicShotDamage, new Color(1f, 0.85f, 0f));
                }
                else
                {
                    RaidGameManager.Instance.SetAnnouncement("MOVE TO THE GOLDEN SYNC LANE BEFORE FIRING RELIC!", 2.5f);
                    RaidNetworkServer.Instance.SendToClient(ClientId, "{\"text\":\"MOVE TO GOLDEN LANE FIRST!\",\"vibrate\":200}");
                }
            }
        }
    }

    // ローカルデバッグ操作（キーボード）を読み取る
    private void ReadLocalKeyboardInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            float left = (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) ? -1f : 0f;
            float right = (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) ? 1f : 0f;
            inputX = left + right;
            isFiring = Keyboard.current.spaceKey.isPressed;
            if (Keyboard.current.eKey.wasPressedThisFrame) triggerRelic = true;
            return;
        }
#endif
        try
        {
            inputX = Input.GetAxisRaw("Horizontal");
            isFiring = Input.GetKey(KeyCode.Space);
            if (Input.GetKeyDown(KeyCode.E)) triggerRelic = true;
        }
        catch { }
    }

    // 敵弾被弾時の処理（硬直、シールド減少、演出）
    public void OnHitByEnemy()
    {
        if (stunTimer > 0f) return;
        stunTimer = hitStunDuration;
        RaidGameManager.Instance.DamageCityShield(hitCityShieldDamage);
        if (RaidJuiceManager.Instance != null)
        {
            RaidJuiceManager.Instance.SpawnExplosion(transform.position, Color.red, 18, 7f, 0.25f, true);
        }
        RaidNetworkServer.Instance.SendToClient(ClientId, "{\"vibrate\":250}");
    }

    // 機体属性を設定し、見た目とスマホ表示を更新
    public void SetElement(ElementColor element)
    {
        CurrentElement = element;
        string hex = "#12141c";
        string txt = $"[{ClientId}] CLEAR INVADERS";

        if (element == ElementColor.Red)
        {
            sr.color = new Color(1f, 0.28f, 0.28f);
            hex = "#5a0b0b";
            txt = $"[{ClientId}] RED ROLE: SHOOT RED CORE!";
        }
        else if (element == ElementColor.Blue)
        {
            sr.color = new Color(0.25f, 0.65f, 1f);
            hex = "#0b245a";
            txt = $"[{ClientId}] BLUE ROLE: SHOOT BLUE CORE!";
        }
        else
        {
            sr.color = HasRelic ? new Color(1f, 0.85f, 0.1f) : Color.white;
        }

        if (labelMesh != null) labelMesh.text = HasRelic ? $"★{ClientId}★" : ClientId;
        if (RaidNetworkServer.Instance != null)
        {
            RaidNetworkServer.Instance.SendToClient(ClientId, $"{{\"color\":\"{hex}\",\"text\":\"{txt}\",\"vibrate\":120}}");
        }
    }

    // RaidGameManagerからプレイヤー調整値を適用する
    public void ConfigureCombatParameters(
        float configuredMoveSpeed,
        float configuredXLimit,
        float configuredBaseFireInterval,
        float configuredNormalShotDamage,
        float configuredDpsShotDamage,
        float configuredDpsSideDamageRate,
        float configuredShotSpeedY,
        float configuredDpsSideShotSpeedX,
        float configuredDpsSideShotSpeedY,
        float configuredRelicShotSpeedY,
        float configuredRelicShotDamage,
        float configuredHitStunDuration,
        float configuredHitCityShieldDamage)
    {
        moveSpeed = configuredMoveSpeed;
        xLimit = configuredXLimit;
        baseFireInterval = configuredBaseFireInterval;
        normalShotDamage = configuredNormalShotDamage;
        dpsShotDamage = configuredDpsShotDamage;
        dpsSideDamageRate = configuredDpsSideDamageRate;
        shotSpeedY = configuredShotSpeedY;
        dpsSideShotSpeedX = configuredDpsSideShotSpeedX;
        dpsSideShotSpeedY = configuredDpsSideShotSpeedY;
        relicShotSpeedY = configuredRelicShotSpeedY;
        relicShotDamage = configuredRelicShotDamage;
        hitStunDuration = configuredHitStunDuration;
        hitCityShieldDamage = configuredHitCityShieldDamage;
    }

    // レリック所持状態を設定し、見た目とスマホ表示を更新
    public void SetRelicState(bool hasRelic)
    {
        HasRelic = hasRelic;
        if (hasRelic)
        {
            sr.color = new Color(1f, 0.85f, 0.1f);
            if (labelMesh != null) labelMesh.text = $"★RELIC:{ClientId}★";
            RaidNetworkServer.Instance.SendToClient(ClientId, "{\"color\":\"#775500\",\"relic\":true,\"text\":\"ENTER GOLDEN LANE & PRESS RELIC BUTTON!\",\"vibrate\":400}");
        }
        else
        {
            sr.color = Color.white;
            if (labelMesh != null) labelMesh.text = ClientId;
            RaidNetworkServer.Instance.SendToClient(ClientId, "{\"relic\":false}");
        }
    }
}

public class RaidBullet : MonoBehaviour
{
    // シーン上の弾を一括参照するためのリスト
    public static readonly List<RaidBullet> ActiveBullets = new List<RaidBullet>();

    public ElementColor Element { get; private set; } // 弾の属性
    public bool IsRelic { get; private set; } // レリック弾かどうか
    public bool IsEnemyBullet { get; private set; } // 敵弾かどうか
    public float Damage { get; private set; } // 与えるダメージ量

    [Header("弾パラメータ")]
    [SerializeField] private float lifeTime = 3.5f; // 弾が自動消滅するまでの時間

    private Vector3 velocity; // 弾の速度ベクトル
    private Color bulletColor; // トレイル演出に使う色

    // 弾オブジェクトを生成して初期化
    public static void Spawn(Vector3 pos, Vector3 vel, ElementColor elem, bool isRelic, bool isEnemy, float dmg, Color col)
    {
        // 味方弾・敵弾・レリック弾を同一クラスで生成
        GameObject go = new GameObject(isRelic ? "RelicLaser" : "Bullet");
        go.transform.position = pos;
        go.transform.localScale = isRelic ? new Vector3(0.85f, 2.8f, 1f) : (isEnemy ? new Vector3(0.24f, 0.5f, 1f) : new Vector3(0.2f, 0.7f, 1f));

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RaidSpriteFactory.GetWhiteSquare();
        sr.color = col;
        sr.sortingOrder = 8;

        RaidBullet b = go.AddComponent<RaidBullet>();
        b.velocity = vel;
        b.Element = elem;
        b.IsRelic = isRelic;
        b.IsEnemyBullet = isEnemy;
        b.Damage = dmg;
        b.bulletColor = col;
        ActiveBullets.Add(b);
    }

    // 破棄時に管理リストから取り除く
    private void OnDestroy()
    {
        ActiveBullets.Remove(this);
    }

    // 弾の移動・寿命・当たり判定を更新
    private void Update()
    {
        transform.position += velocity * Time.deltaTime;
        lifeTime -= Time.deltaTime;

        // 弾のネオン残像トレイル
        if (RaidJuiceManager.Instance != null && Random.value < (IsRelic ? 1f : 0.45f))
        {
            RaidJuiceManager.Instance.SpawnTrail(transform.position, bulletColor, IsRelic ? 0.55f : 0.16f);
        }

        if (lifeTime <= 0f || Mathf.Abs(transform.position.y) > 7f)
        {
            Destroy(gameObject);
            return;
        }

        if (IsEnemyBullet)
        {
            foreach (var p in RaidGameManager.Instance.GetAllPlayers())
            {
                if (p == null) continue;
                if (Vector2.Distance(transform.position, p.transform.position) < 0.5f)
                {
                    p.OnHitByEnemy();
                    Destroy(gameObject);
                    return;
                }
            }
        }
        else
        {
            if (InvaderSwarm.Instance != null && InvaderSwarm.Instance.CheckBulletHit(this))
            {
                if (!IsRelic) { Destroy(gameObject); return; }
            }

            if (RaidBoss.Instance != null && RaidBoss.Instance.CheckBulletHit(this))
            {
                Destroy(gameObject);
                return;
            }
        }
    }
}