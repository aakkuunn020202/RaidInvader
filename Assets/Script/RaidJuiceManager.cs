using System.Collections.Generic;
using UnityEngine;

public class RaidJuiceManager : MonoBehaviour
{
    // 演出管理のシングルトン
    public static RaidJuiceManager Instance { get; private set; }

    private class ParticleItem
    {
        public Transform trans;
        public SpriteRenderer sr;
        public Vector3 vel;
        public float life, maxLife;
        public float startScale, endScale;
        public float rotSpeed;
    }

    private class PopupItem
    {
        public Vector3 worldPos;
        public Vector3 vel;
        public string text;
        public Color color;
        public int baseFontSize;
        public float life, maxLife;
    }

    private readonly List<ParticleItem> particles = new List<ParticleItem>(); // 画面上のパーティクル一覧
    private readonly List<PopupItem> popups = new List<PopupItem>(); // ダメージポップアップ一覧

    private Material additiveMat; // 加算合成マテリアル
    private Camera mainCam; // メインカメラ参照
    [Header("カメラ演出パラメータ")]
    [SerializeField] private Vector3 camBasePos = new Vector3(0f, 0f, -10f); // カメラ基準位置
    [SerializeField] private float dpsOrthoSize = 5.25f; // DPS時カメラサイズ
    [SerializeField] private float normalOrthoSize = 5.6f; // 通常時カメラサイズ
    private float shakePower; // 現在のシェイク強度
    private float flashAlpha; // フラッシュ透明度
    private Color flashColor = Color.white; // フラッシュ色
    private float hitStopTimer; // ヒットストップ残り時間

    // コンボ＆トータルダメージ（ドーパミンカウンター）
    public int ComboCount { get; private set; }
    private float comboDecayTimer;
    public long TotalDamageDealt { get; private set; }

    // 合成効果音用
    private AudioSource audioSrc;
    private AudioClip sfxShoot, sfxHit, sfxCrit, sfxExplosion, sfxShieldBreak;

    // インスタンスが無ければ生成
    public static void EnsureInstance()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("RaidJuiceManager");
        Instance = go.AddComponent<RaidJuiceManager>();
    }

    // 初期化（素材・音・背景生成）
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        mainCam = Camera.main;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            additiveMat = new Material(sh);
            additiveMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            additiveMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // 加算合成（光る表現）
        }

        SetupSynthAudio();
        SpawnStarfieldBackground();
    }

    // 合成効果音を事前生成
    private void SetupSynthAudio()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.volume = 0.55f;

        sfxShoot = CreateSynthClip("Shoot", 0.06f, 880f, 220f, 0.1f);
        sfxHit = CreateSynthClip("Hit", 0.07f, 520f, 140f, 0.35f);
        sfxCrit = CreateSynthClip("Crit", 0.11f, 1400f, 680f, 0.2f);
        sfxExplosion = CreateSynthClip("Explosion", 0.28f, 240f, 35f, 0.85f);
        sfxShieldBreak = CreateSynthClip("ShieldBreak", 0.65f, 950f, 60f, 0.65f);
    }

    // 簡易シンセ波形でAudioClipを生成
    private AudioClip CreateSynthClip(string clipName, float duration, float startFreq, float endFreq, float noiseMix)
    {
        int sampleRate = 22050;
        int samples = Mathf.Max(1, (int)(sampleRate * duration));
        float[] data = new float[samples];
        float phase = 0f;
        System.Random rng = new System.Random(clipName.GetHashCode());

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float env = Mathf.Pow(1f - t, 1.4f);
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += freq / sampleRate;
            float tone = Mathf.Sin(phase * Mathf.PI * 2f) >= 0f ? 0.8f : -0.8f; // 矩形波
            float noise = ((float)rng.NextDouble() * 2f - 1f);
            data[i] = Mathf.Clamp((tone * (1f - noiseMix) + noise * noiseMix) * env * 0.45f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(clipName, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 背景の星屑を生成
    private void SpawnStarfieldBackground()
    {
        // 背景を高速で流れるネオンスターダスト
        for (int i = 0; i < 35; i++)
        {
            Vector3 pos = new Vector3(Random.Range(-9f, 9f), Random.Range(-6f, 6f), 0f);
            SpawnSingleParticle(pos, new Vector3(0f, Random.Range(-12f, -4f), 0f), new Color(0.3f, 0.6f, 1f, 0.35f), 0.08f, 0.6f, 999f, true);
        }
    }

    // 演出システム全体の更新
    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // ヒットストップ管理
        if (hitStopTimer > 0f)
        {
            hitStopTimer -= dt;
            if (hitStopTimer <= 0f) Time.timeScale = 1f;
        }

        // カメラシェイク＆DPSズーム演出
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam != null)
        {
            bool isDps = RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.DpsPhase;
            float targetOrtho = isDps ? dpsOrthoSize : normalOrthoSize;
            mainCam.orthographicSize = Mathf.Lerp(mainCam.orthographicSize, targetOrtho, dt * 6f);

            if (shakePower > 0.005f)
            {
                Vector2 offset = Random.insideUnitCircle * shakePower;
                mainCam.transform.position = camBasePos + new Vector3(offset.x, offset.y, 0f);
                shakePower = Mathf.Lerp(shakePower, 0f, dt * 14f);
            }
            else
            {
                mainCam.transform.position = camBasePos;
                shakePower = 0f;
            }
        }

        // 画面フラッシュ減衰
        if (flashAlpha > 0f) flashAlpha = Mathf.Max(0f, flashAlpha - dt * 2.8f);

        // コンボ減衰
        if (comboDecayTimer > 0f)
        {
            comboDecayTimer -= dt;
            if (comboDecayTimer <= 0f) ComboCount = 0;
        }

        // パーティクル更新
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            if (p.trans == null) { particles.RemoveAt(i); continue; }

            if (p.maxLife >= 900f)
            {
                // 背景の星ループ
                float speedMult = (RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.DpsPhase) ? 2.8f : 1f;
                p.trans.position += p.vel * (dt * speedMult);
                if (p.trans.position.y < -6.2f)
                {
                    p.trans.position = new Vector3(Random.Range(-9f, 9f), 6.2f, 0f);
                }
                continue;
            }

            p.life -= dt;
            if (p.life <= 0f)
            {
                Destroy(p.trans.gameObject);
                particles.RemoveAt(i);
                continue;
            }

            float ratio = p.life / p.maxLife;
            p.trans.position += p.vel * dt;
            p.vel *= (1f - dt * 2.5f);
            p.trans.Rotate(0f, 0f, p.rotSpeed * dt);

            float sc = Mathf.Lerp(p.endScale, p.startScale, ratio);
            p.trans.localScale = new Vector3(sc, sc, 1f);

            Color c = p.sr.color;
            c.a = ratio;
            p.sr.color = c;
        }

        // ダメージポップアップ更新
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var pop = popups[i];
            pop.life -= dt;
            if (pop.life <= 0f) { popups.RemoveAt(i); continue; }
            pop.worldPos += pop.vel * dt;
            pop.vel.y -= 9.5f * dt; // 重力で跳ねて落ちる
        }
    }

    // 汎用パーティクルを1つ生成
    private void SpawnSingleParticle(Vector3 pos, Vector3 vel, Color col, float startScale, float endScale, float life, bool isBg = false)
    {
        if (particles.Count > 260 && !isBg) return;

        GameObject go = new GameObject(isBg ? "Star" : "VFX_Particle");
        go.transform.position = pos;
        go.transform.localScale = new Vector3(startScale, startScale, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RaidSpriteFactory.GetWhiteSquare();
        sr.color = col;
        sr.sortingOrder = isBg ? 0 : 15;
        if (additiveMat != null) sr.sharedMaterial = additiveMat;

        particles.Add(new ParticleItem
        {
            trans = go.transform,
            sr = sr,
            vel = vel,
            life = life,
            maxLife = life,
            startScale = startScale,
            endScale = endScale,
            rotSpeed = isBg ? 0f : Random.Range(-540f, 540f)
        });
    }

    // 残像・軌跡パーティクル
    public void SpawnTrail(Vector3 pos, Color col, float size = 0.22f)
    {
        SpawnSingleParticle(pos, Random.insideUnitCircle * 0.6f, col, size, 0.02f, 0.16f);
    }

    // 爆散エフェクト＋ショックウェーブリング
    public void SpawnExplosion(Vector3 pos, Color col, int count = 22, float speed = 9f, float shake = 0.22f, bool bigExplosion = false)
    {
        shakePower = Mathf.Max(shakePower, shake);
        if (audioSrc != null) audioSrc.PlayOneShot(bigExplosion ? sfxExplosion : sfxHit, bigExplosion ? 0.9f : 0.45f);

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float spd = Random.Range(speed * 0.25f, speed);
            Vector3 vel = new Vector3(Mathf.Cos(angle) * spd, Mathf.Sin(angle) * spd, 0f);
            Color c = Color.Lerp(col, Color.white, Random.Range(0f, 0.45f));
            SpawnSingleParticle(pos, vel, c, Random.Range(0.22f, 0.48f), 0.02f, Random.Range(0.22f, 0.52f));
        }

        // 衝撃波リング表現（膨張する四角形）
        if (bigExplosion)
        {
            SpawnSingleParticle(pos, Vector3.zero, new Color(col.r, col.g, col.b, 0.7f), 0.3f, 3.2f, 0.25f);
        }
    }

    // 極太レリックレーザー演出
    public void SpawnMegaLaserBeam(float xPos, Color col)
    {
        for (int i = 0; i < 35; i++)
        {
            float y = Mathf.Lerp(-4.2f, 4.2f, i / 34f);
            Vector3 pos = new Vector3(xPos + Random.Range(-0.15f, 0.15f), y, 0f);
            Vector3 vel = new Vector3(Random.Range(-4f, 4f), Random.Range(2f, 8f), 0f);
            SpawnSingleParticle(pos, vel, col, 0.95f, 0.05f, 0.4f);
        }
        TriggerScreenFlash(col, 0.65f);
        TriggerHitStop(0.12f);
        shakePower = 0.65f;
        if (audioSrc != null) audioSrc.PlayOneShot(sfxShieldBreak, 1f);
    }

    // Destiny 2風のダメージ数字ポップアップ＆コンボ加算
    public void RegisterDamageHit(Vector3 pos, float rawDamage, bool isCrit, string overrideText = null)
    {
        ComboCount++;
        comboDecayTimer = 3.2f;

        // ドパガキ仕様：見栄えのために数値をインフレ表示（12 -> 12,480!!）
        long inflated = (long)(rawDamage * (isCrit ? 1450f : 680f) + Random.Range(10, 99));
        TotalDamageDealt += inflated;

        if (audioSrc != null && isCrit) audioSrc.PlayOneShot(sfxCrit, 0.55f);

        if (popups.Count > 45) popups.RemoveAt(0);
        popups.Add(new PopupItem
        {
            worldPos = pos + new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(0f, 0.4f), 0f),
            vel = new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(3.8f, 6.2f), 0f),
            text = overrideText ?? (isCrit ? $"{inflated:N0}!!" : $"{inflated:N0}"),
            color = isCrit ? new Color(1f, 0.92f, 0.1f) : Color.white,
            baseFontSize = isCrit ? 28 : 19,
            life = 0.75f,
            maxLife = 0.75f
        });
    }

    // 射撃音をランダム間引きで再生
    public void PlayShootSound()
    {
        if (audioSrc != null && Random.value < 0.35f) audioSrc.PlayOneShot(sfxShoot, 0.2f);
    }

    // 画面フラッシュを発生
    public void TriggerScreenFlash(Color col, float alpha = 0.55f)
    {
        flashColor = col;
        flashAlpha = alpha;
    }

    // 短時間のヒットストップを発生
    public void TriggerHitStop(float duration = 0.08f)
    {
        hitStopTimer = duration;
        Time.timeScale = 0.05f;
    }

    // 演出UI（フラッシュ、ダメージ、コンボ）を描画
    private void OnGUI()
    {
        if (mainCam == null) return;

        // 1. 全画面フラッシュ演出
        if (flashAlpha > 0.01f)
        {
            Color prev = GUI.color;
            GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        // 2. 浮き上がるダメージ数値の描画
        GUIStyle dmgStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        foreach (var pop in popups)
        {
            Vector3 sp = mainCam.WorldToScreenPoint(pop.worldPos);
            if (sp.z < 0f) continue;
            float guiY = Screen.height - sp.y;
            float ratio = pop.life / pop.maxLife;

            dmgStyle.fontSize = Mathf.RoundToInt(pop.baseFontSize * (0.8f + ratio * 0.4f));

            // 黒縁取りシャドウ
            dmgStyle.normal.textColor = new Color(0f, 0f, 0f, ratio);
            GUI.Label(new Rect(sp.x - 98, guiY - 23, 200, 50), pop.text, dmgStyle);

            // 本体カラー
            Color c = pop.color;
            c.a = ratio;
            dmgStyle.normal.textColor = c;
            GUI.Label(new Rect(sp.x - 100, guiY - 25, 200, 50), pop.text, dmgStyle);
        }

        // 3. 右上：ドパガキ用「超インフレ・コンボ＆トータルダメージカウンター」
        if (ComboCount > 1)
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 18f) * 0.12f;
            GUIStyle comboStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Mathf.Min(48, 24 + ComboCount / 3) * pulse)
            };
            Color rainbow = Color.HSVToRGB((Time.unscaledTime * 0.8f) % 1f, 0.75f, 1f);
            comboStyle.normal.textColor = rainbow;

            GUI.Label(new Rect(Screen.width - 420, 20, 400, 60), $"{ComboCount} HIT COMBO!!", comboStyle);

            GUIStyle totalStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                fontSize = 18
            };
            totalStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.Label(new Rect(Screen.width - 420, 75, 400, 30), $"TOTAL DAMAGE: {TotalDamageDealt:N0}", totalStyle);
        }

        // 4. DPSフェーズ中の画面枠レインボー点滅演出
        if (RaidBoss.Instance != null && RaidBoss.Instance.CurrentPhase == RaidPhase.DpsPhase)
        {
            Color feverCol = Color.HSVToRGB((Time.unscaledTime * 1.5f) % 1f, 0.85f, 1f);
            feverCol.a = 0.35f + Mathf.Sin(Time.unscaledTime * 20f) * 0.15f;
            Color prev = GUI.color;
            GUI.color = feverCol;
            int border = 10;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, border), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - border, Screen.width, border), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 0, border, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - border, 0, border, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}