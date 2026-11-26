using System.Text;

namespace LanDrop.Utils;

/// <summary>
/// URL表示用の最小限のQRコードエンコーダ
/// </summary>
/// <remarks>
/// バイトモード・誤り訂正レベルM・バージョン1〜10（最大213バイト）のみ対応する。
/// 外部依存を増やさないための自前実装で、構造は JIS X 0510 / ISO/IEC 18004 に従う。
/// </remarks>
public sealed class QrCode
{
    // バージョンごとの誤り訂正レベルMのブロック構成
    // (1ブロックあたりの誤り訂正コード語数, グループ1のブロック数, グループ1のデータコード語数, グループ2のブロック数, グループ2のデータコード語数)
    private static readonly (int Ec, int B1, int D1, int B2, int D2)[] BlocksM =
    [
        default,
        (10, 1, 16, 0, 0),
        (16, 1, 28, 0, 0),
        (26, 1, 44, 0, 0),
        (18, 2, 32, 0, 0),
        (24, 2, 43, 0, 0),
        (16, 4, 27, 0, 0),
        (18, 4, 31, 0, 0),
        (22, 2, 38, 2, 39),
        (22, 3, 36, 2, 37),
        (26, 4, 43, 1, 44),
    ];

    private static readonly int[][] AlignmentPositions =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    public const int MaxVersion = 10;

    private readonly bool[,] _modules;    // [y, x] true=暗
    private readonly bool[,] _isFunction;

    /// <summary>バージョン（1〜10）</summary>
    public int Version { get; }

    /// <summary>一辺のモジュール数</summary>
    public int Size { get; }

    /// <summary>適用したマスク番号（0〜7）</summary>
    public int Mask { get; }

    /// <summary>機能パターン（位置検出・タイミング・形式情報など）のモジュールか</summary>
    internal bool IsFunctionModule(int x, int y) => _isFunction[y, x];

    /// <summary>モジュールが暗（黒）か</summary>
    public bool this[int x, int y] => _modules[y, x];

    private QrCode(int version, byte[] dataCodewords, int? forcedMask)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        var allCodewords = AddErrorCorrectionAndInterleave(dataCodewords);
        DrawCodewords(allCodewords);

        if (forcedMask is int m)
        {
            Mask = m;
        }
        else
        {
            // ペナルティが最小のマスクを選ぶ
            var best = 0;
            var minPenalty = int.MaxValue;
            for (var i = 0; i < 8; i++)
            {
                ApplyMask(i);
                DrawFormatBits(i);
                var penalty = GetPenaltyScore();
                if (penalty < minPenalty)
                {
                    best = i;
                    minPenalty = penalty;
                }
                ApplyMask(i); // XORなので元に戻る
            }
            Mask = best;
        }

        ApplyMask(Mask);
        DrawFormatBits(Mask);
    }

    /// <summary>
    /// テキストをQRコードにする。長すぎる場合は null
    /// </summary>
    /// <param name="text">符号化するテキスト（UTF-8）</param>
    /// <param name="forcedMask">テスト用: マスク番号を固定する</param>
    /// <param name="minVersion">テスト用: 最小バージョン</param>
    public static QrCode? Encode(string text, int? forcedMask = null, int minVersion = 1)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        for (var version = Math.Max(1, minVersion); version <= MaxVersion; version++)
        {
            var (_, b1, d1, b2, d2) = BlocksM[version];
            var capacityBits = (b1 * d1 + b2 * d2) * 8;
            var countBits = version <= 9 ? 8 : 16;
            var usedBits = 4 + countBits + bytes.Length * 8;
            if (usedBits > capacityBits)
                continue;

            var bits = new List<bool>(capacityBits);
            AppendBits(bits, 0b0100, 4);           // バイトモード
            AppendBits(bits, bytes.Length, countBits);
            foreach (var b in bytes)
                AppendBits(bits, b, 8);

            // 終端パターンとバイト境界までの0埋め
            AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);

            var data = new byte[capacityBits / 8];
            for (var i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                    data[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
            // 埋め草コード語
            for (int i = bits.Count / 8, pad = 0; i < data.Length; i++, pad ^= 1)
                data[i] = pad == 0 ? (byte)0xEC : (byte)0x11;

            return new QrCode(version, data, forcedMask);
        }

        return null;
    }

    private static void AppendBits(List<bool> bits, int value, int length)
    {
        for (var i = length - 1; i >= 0; i--)
            bits.Add(((value >> i) & 1) != 0);
    }

    // ── 機能パターン ─────────────────────────────────────

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        // タイミングパターン
        for (var i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        // 位置検出パターン（分離パターン含む）
        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        // 位置合わせパターン
        var pos = AlignmentPositions[Version];
        var n = pos.Length;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0))
                    continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        SetFunction(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }

        // 形式情報の領域を予約（値は後で書く）
        DrawFormatBits(0);
        DrawVersion();
    }

    private void DrawFinder(int cx, int cy)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || x >= Size || y < 0 || y >= Size)
                    continue;
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                SetFunction(x, y, dist != 2 && dist != 4);
            }
        }
    }

    /// <summary>
    /// 形式情報（誤り訂正レベルM + マスク番号、BCH(15,5)、マスク 0x5412 適用済み）
    /// </summary>
    internal static int FormatBits(int mask)
    {
        // 誤り訂正レベルM = 00
        var data = (0 << 3) | mask;
        var rem = data;
        for (var i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        return ((data << 10) | rem) ^ 0x5412;
    }

    /// <summary>
    /// 型番情報（BCH(18,6)）
    /// </summary>
    internal static int VersionBits(int version)
    {
        var rem = version;
        for (var i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        return (version << 12) | rem;
    }

    private void DrawFormatBits(int mask)
    {
        var bits = FormatBits(mask);

        static bool Bit(int v, int i) => ((v >> i) & 1) != 0;

        // 左上
        for (var i = 0; i <= 5; i++)
            SetFunction(8, i, Bit(bits, i));
        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (var i = 9; i < 15; i++)
            SetFunction(14 - i, 8, Bit(bits, i));

        // 右上・左下
        for (var i = 0; i < 8; i++)
            SetFunction(Size - 1 - i, 8, Bit(bits, i));
        for (var i = 8; i < 15; i++)
            SetFunction(8, Size - 15 + i, Bit(bits, i));
        SetFunction(8, Size - 8, true); // 常に暗のモジュール
    }

    private void DrawVersion()
    {
        if (Version < 7)
            return;

        var bits = VersionBits(Version);

        for (var i = 0; i < 18; i++)
        {
            var dark = ((bits >> i) & 1) != 0;
            var a = Size - 11 + i % 3;
            var b = i / 3;
            SetFunction(a, b, dark);
            SetFunction(b, a, dark);
        }
    }

    // ── 誤り訂正 ─────────────────────────────────────────

    private byte[] AddErrorCorrectionAndInterleave(byte[] data)
    {
        var (ec, b1, d1, b2, d2) = BlocksM[Version];
        var generator = ReedSolomonGenerator(ec);

        var dataBlocks = new List<byte[]>();
        var ecBlocks = new List<byte[]>();
        var offset = 0;
        for (var i = 0; i < b1 + b2; i++)
        {
            var len = i < b1 ? d1 : d2;
            var block = data.AsSpan(offset, len).ToArray();
            offset += len;
            dataBlocks.Add(block);
            ecBlocks.Add(ReedSolomonRemainder(block, generator));
        }

        var result = new List<byte>();
        var maxData = Math.Max(d1, d2);
        for (var i = 0; i < maxData; i++)
            foreach (var block in dataBlocks)
                if (i < block.Length)
                    result.Add(block[i]);
        for (var i = 0; i < ec; i++)
            foreach (var block in ecBlocks)
                result.Add(block[i]);

        return result.ToArray();
    }

    private static byte GfMultiply(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    internal static byte[] ReedSolomonGenerator(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = GfMultiply(result[j], root);
                if (j + 1 < degree)
                    result[j] ^= result[j + 1];
            }
            root = GfMultiply(root, 0x02);
        }
        return result;
    }

    internal static byte[] ReedSolomonRemainder(byte[] data, byte[] generator)
    {
        var result = new byte[generator.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
                result[i] ^= GfMultiply(generator[i], factor);
        }
        return result;
    }

    // ── データ配置とマスク ───────────────────────────────

    private void DrawCodewords(byte[] codewords)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
                right = 5;
            for (var vert = 0; vert < Size; vert++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (!_isFunction[y, x] && i < codewords.Length * 8)
                    {
                        _modules[y, x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (_isFunction[y, x])
                    continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert)
                    _modules[y, x] = !_modules[y, x];
            }
        }
    }

    private int GetPenaltyScore()
    {
        var penalty = 0;

        // N1: 同色の連続（行・列）, N3: 位置検出パターンに似た並び
        for (var pass = 0; pass < 2; pass++)
        {
            for (var a = 0; a < Size; a++)
            {
                var runColor = false;
                var runLength = 0;
                var line = new bool[Size];
                for (var b = 0; b < Size; b++)
                {
                    var dark = pass == 0 ? _modules[a, b] : _modules[b, a];
                    line[b] = dark;
                    if (b > 0 && dark == runColor)
                    {
                        runLength++;
                        if (runLength == 5)
                            penalty += 3;
                        else if (runLength > 5)
                            penalty++;
                    }
                    else
                    {
                        runColor = dark;
                        runLength = 1;
                    }
                }
                penalty += 40 * CountFinderLike(line);
            }
        }

        // N2: 2x2 の同色ブロック
        for (var y = 0; y < Size - 1; y++)
        {
            for (var x = 0; x < Size - 1; x++)
            {
                var c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                    penalty += 3;
            }
        }

        // N4: 暗モジュールの割合
        var darkCount = 0;
        foreach (var m in _modules)
            if (m) darkCount++;
        var total = Size * Size;
        var k = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
        penalty += k * 10;

        return penalty;
    }

    private static int CountFinderLike(bool[] line)
    {
        // 1:1:3:1:1 の暗明パターンの片側に4モジュール以上の明（範囲外は明とみなす）
        bool[] core = [true, false, true, true, true, false, true];
        bool At(int i) => i >= 0 && i < line.Length && line[i];

        var count = 0;
        for (var start = 0; start + core.Length <= line.Length; start++)
        {
            var match = true;
            for (var i = 0; i < core.Length && match; i++)
                match = line[start + i] == core[i];
            if (!match)
                continue;

            var lightBefore = !At(start - 1) && !At(start - 2) && !At(start - 3) && !At(start - 4);
            var lightAfter = !At(start + 7) && !At(start + 8) && !At(start + 9) && !At(start + 10);
            if (lightBefore || lightAfter)
                count++;
        }
        return count;
    }

    // ── 出力 ─────────────────────────────────────────────

    /// <summary>
    /// SVG（インライン埋め込み用）を生成する
    /// </summary>
    /// <param name="quietZone">周囲の余白（モジュール数）</param>
    /// <param name="pixelSize">表示サイズ（px）</param>
    public string ToSvg(int quietZone = 4, int pixelSize = 160)
    {
        var dim = Size + quietZone * 2;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {dim} {dim}\" width=\"{pixelSize}\" height=\"{pixelSize}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR code\">");
        sb.Append($"<rect width=\"{dim}\" height=\"{dim}\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (_modules[y, x])
                    sb.Append($"M{x + quietZone},{y + quietZone}h1v1h-1z");
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    /// <summary>
    /// 端末表示用の文字列（上下2モジュールを1文字に詰める）を生成する
    /// </summary>
    /// <remarks>暗い背景の端末でも読めるよう、ANSIエスケープで白地に黒を明示する。</remarks>
    public IEnumerable<string> ToConsoleLines(int quietZone = 2, string indent = "  ")
    {
        const string Start = "\u001b[30;107m"; // 黒文字・明るい白背景
        const string Reset = "\u001b[0m";
        var dim = Size + quietZone * 2;

        bool Dark(int x, int y)
        {
            x -= quietZone;
            y -= quietZone;
            return x >= 0 && y >= 0 && x < Size && y < Size && _modules[y, x];
        }

        for (var y = 0; y < dim; y += 2)
        {
            var sb = new StringBuilder(indent).Append(Start);
            for (var x = 0; x < dim; x++)
            {
                var top = Dark(x, y);
                var bottom = y + 1 < dim && Dark(x, y + 1);
                sb.Append((top, bottom) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    _ => ' ',
                });
            }
            yield return sb.Append(Reset).ToString();
        }
    }
}
