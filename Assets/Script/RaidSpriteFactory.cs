using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class RaidSpriteFactory
{
    // 使い回しスプライトのキャッシュ
    private static Sprite whiteSquare;
    private static Sprite invaderSprite;
    private static Sprite bossSprite;
    private static Sprite coreSprite;

    // プレイヤーや弾・UIに使う「白い四角形」スプライト
    public static Sprite GetWhiteSquare()
    {
        if (whiteSquare != null) return whiteSquare;
        Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] cols = new Color[64];
        for (int i = 0; i < 64; i++) cols[i] = Color.white;
        tex.SetPixels(cols);
        tex.Apply();
        whiteSquare = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
        return whiteSquare;
    }

    // プレイヤーの見た目を「白い四角形」として返す
    public static Sprite GetPlayerSprite()
    {
        return GetWhiteSquare();
    }

    // インベーダー用スプライトを取得（なければ生成）
    public static Sprite GetInvaderSprite()
    {
        if (invaderSprite != null) return invaderSprite;
        string[] pattern = {
            "..X.....X..",
            "...X...X...",
            "..XXXXXXX..",
            ".XX.XXX.XX.",
            "XXXXXXXXXXX",
            "X.XXXXXXX.X",
            "X.X.....X.X",
            "...XX.XX..."
        };
        invaderSprite = CreateFromAscii(pattern, 14f);
        return invaderSprite;
    }

    // ボス用スプライトを取得（なければ生成）
    public static Sprite GetBossSprite()
    {
        if (bossSprite != null) return bossSprite;
        string[] pattern = {
            "....XXXXXXXXXXXX....",
            "..XXXXXXXXXXXXXXXX..",
            ".XXXXXXXXXXXXXXXXXX.",
            "XXX..XXXXXXXXXX..XXX",
            "XXX..XXXXXXXXXX..XXX",
            "XXXXXXXXXXXXXXXXXXXX",
            ".XXXXXXXXXXXXXXXXXX.",
            "..XX.XXXXXXXXXX.XX..",
            ".XX...XX....XX...XX.",
            "XX.....XX..XX.....XX"
        };
        bossSprite = CreateFromAscii(pattern, 6f);
        return bossSprite;
    }

    // コア用スプライトを取得（なければ生成）
    public static Sprite GetCoreSprite()
    {
        if (coreSprite != null) return coreSprite;
        string[] pattern = {
            "..XXXX..",
            ".XXXXXX.",
            "XX.XX.XX",
            "XXXXXXXX",
            "XXXXXXXX",
            "XX.XX.XX",
            ".XXXXXX.",
            "..XXXX.."
        };
        coreSprite = CreateFromAscii(pattern, 10f);
        return coreSprite;
    }

    private static Sprite CreateFromAscii(string[] rows, float pixelsPerUnit)
    {
        // 文字列パターン（X=塗る）からドット絵スプライトを生成
        int height = rows.Length;
        int width = rows[0].Length;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        for (int y = 0; y < height; y++)
        {
            string row = rows[height - 1 - y];
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, y, row[x] == 'X' ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }

    // URL文字列からQRコード(Version 3-L, 29x29)のTexture2Dを純C#で生成
    public static Texture2D GenerateQrCodeTexture(string url)
    {
        byte[] urlBytes = Encoding.UTF8.GetBytes(url);
        int size = 29; // Version 3 (最大53文字のURLに対応)
        int dataCodewords = 55;
        int ecCodewords = 15;

        // 1. データビット列の構築 (Byte mode = 0100)
        List<int> bits = new List<int>();
        AddBits(bits, 0b0100, 4);
        AddBits(bits, urlBytes.Length, 8);
        foreach (byte b in urlBytes) AddBits(bits, b, 8);
        for (int i = 0; i < 4 && bits.Count < dataCodewords * 8; i++) bits.Add(0);
        while (bits.Count % 8 != 0) bits.Add(0);

        byte[] data = new byte[dataCodewords];
        for (int i = 0; i < bits.Count / 8; i++)
        {
            int val = 0;
            for (int b = 0; b < 8; b++) val = (val << 1) | bits[i * 8 + b];
            data[i] = (byte)val;
        }
        byte[] padBytes = { 0xEC, 0x11 };
        for (int i = bits.Count / 8, p = 0; i < dataCodewords; i++, p ^= 1) data[i] = padBytes[p];

        // 2. リードソロモン誤り訂正符号の計算
        byte[] ec = ComputeReedSolomon(data, ecCodewords);
        byte[] allCodewords = new byte[dataCodewords + ecCodewords];
        Array.Copy(data, 0, allCodewords, 0, dataCodewords);
        Array.Copy(ec, 0, allCodewords, dataCodewords, ecCodewords);

        // 3. マトリックス構築
        bool[,] modules = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        PlaceFinderPattern(modules, reserved, 0, 0, size);
        PlaceFinderPattern(modules, reserved, size - 7, 0, size);
        PlaceFinderPattern(modules, reserved, 0, size - 7, size);
        PlaceAlignmentPattern(modules, reserved, 22, 22);

        for (int i = 8; i < size - 8; i++)
        {
            modules[6, i] = (i % 2 == 0); reserved[6, i] = true;
            modules[i, 6] = (i % 2 == 0); reserved[i, 6] = true;
        }

        // ダークモジュールとフォーマット予約領域
        modules[size - 8, 8] = true; reserved[size - 8, 8] = true;
        for (int i = 0; i < 9; i++)
        {
            reserved[8, i] = true;
            reserved[i, 8] = true;
        }
        for (int i = 0; i < 8; i++)
        {
            reserved[8, size - 1 - i] = true;
            reserved[size - 1 - i, 8] = true;
        }

        // 4. データビット配置とマスク0適用
        int bitIdx = 0;
        int totalBits = allCodewords.Length * 8;
        bool up = true;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int v = 0; v < size; v++)
            {
                int y = up ? (size - 1 - v) : v;
                for (int dx = 0; dx < 2; dx++)
                {
                    int x = right - dx;
                    if (!reserved[y, x])
                    {
                        bool bit = false;
                        if (bitIdx < totalBits)
                        {
                            bit = ((allCodewords[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                            bitIdx++;
                        }
                        if ((y + x) % 2 == 0) bit = !bit; // Mask 0
                        modules[y, x] = bit;
                    }
                }
            }
            up = !up;
        }

        // 5. フォーマット情報の書き込み (Level L, Mask 0 -> 0x77C4)
        int formatBits = 0x77C4;
        int[] fy = { 0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8 };
        int[] fx = { 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0 };
        for (int i = 0; i < 15; i++)
        {
            bool b = ((formatBits >> i) & 1) != 0;
            modules[fy[i], fx[i]] = b;
            if (i < 8) modules[8, size - 1 - i] = b;
            else modules[size - 7 + (i - 8), 8] = b;
        }

        // 6. 白枠(クワイエットゾーン4マス)付きでTexture2D化
        int margin = 4;
        int texSize = size + margin * 2;
        Texture2D qrTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        qrTex.filterMode = FilterMode.Point;

        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                int mx = x - margin;
                int my = y - margin;
                bool isBlack = (mx >= 0 && mx < size && my >= 0 && my < size) && modules[my, mx];
                qrTex.SetPixel(x, texSize - 1 - y, isBlack ? Color.black : Color.white);
            }
        }
        qrTex.Apply();
        return qrTex;
    }

    // 指定値をビット列として追記
    private static void AddBits(List<int> list, int val, int count)
    {
        for (int i = count - 1; i >= 0; i--) list.Add((val >> i) & 1);
    }

    // ファインダーパターンを配置
    private static void PlaceFinderPattern(bool[,] m, bool[,] r, int ox, int oy, int size)
    {
        for (int dy = -1; dy <= 7; dy++)
        {
            for (int dx = -1; dx <= 7; dx++)
            {
                int x = ox + dx, y = oy + dy;
                if (x < 0 || x >= size || y < 0 || y >= size) continue;
                bool inOuter = (dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6);
                bool onBorder = (dx == 0 || dx == 6 || dy == 0 || dy == 6);
                bool inInner = (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4);
                m[y, x] = inOuter && (onBorder || inInner);
                r[y, x] = true;
            }
        }
    }

    // アライメントパターンを配置
    private static void PlaceAlignmentPattern(bool[,] m, bool[,] r, int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cx + dx, y = cy + dy;
                m[y, x] = (Mathf.Abs(dx) == 2 || Mathf.Abs(dy) == 2 || (dx == 0 && dy == 0));
                r[y, x] = true;
            }
        }
    }

    // QRの誤り訂正符号（Reed-Solomon）を算出
    private static byte[] ComputeReedSolomon(byte[] data, int ecCount)
    {
        byte[] gen = new byte[ecCount + 1];
        gen[0] = 1;
        int root = 1;
        for (int i = 0; i < ecCount; i++)
        {
            for (int j = i + 1; j >= 1; j--)
                gen[j] = (byte)(gen[j] ^ GfMul(gen[j - 1], (byte)root));
            root = GfMul((byte)root, 2);
        }

        byte[] ec = new byte[ecCount];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ ec[0]);
            Array.Copy(ec, 1, ec, 0, ecCount - 1);
            ec[ecCount - 1] = 0;
            for (int j = 0; j < ecCount; j++)
                ec[j] ^= GfMul(gen[j + 1], factor);
        }
        return ec;
    }

    // GF(256)上の乗算
    private static byte GfMul(byte a, byte b)
    {
        int p = 0, aa = a, bb = b;
        for (int i = 0; i < 8; i++)
        {
            if ((bb & 1) != 0) p ^= aa;
            bool carry = (aa & 0x80) != 0;
            aa = (aa << 1) & 0xFF;
            if (carry) aa ^= 0x1D;
            bb >>= 1;
        }
        return (byte)p;
    }
}