using System.Text;
using LanDrop.Utils;

namespace LanDrop.Tests;

/// <summary>
/// QrCode のテスト
/// - 公開されている既知の値（Reed-Solomon の計算例、形式情報・型番情報の表）との一致
/// - 生成したシンボルを読み戻して元のテキストに戻ること
/// </summary>
public class QrCodeTests
{
    // ── 既知の値 ─────────────────────────────────────────

    [Fact]
    public void ReedSolomon_MatchesKnownExample()
    {
        // "HELLO WORLD" 1-M のデータコード語と誤り訂正コード語（thonky.com QR Code Tutorial の計算例）
        byte[] data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];
        byte[] expected = [196, 35, 39, 119, 235, 215, 231, 226, 93, 23];

        var ec = QrCode.ReedSolomonRemainder(data, QrCode.ReedSolomonGenerator(10));

        Assert.Equal(expected, ec);
    }

    [Theory]
    // 誤り訂正レベルM の形式情報（ISO/IEC 18004 の表）
    [InlineData(0, "101010000010010")]
    [InlineData(1, "101000100100101")]
    [InlineData(2, "101111001111100")]
    [InlineData(3, "101101101001011")]
    [InlineData(4, "100010111111001")]
    [InlineData(5, "100000011001110")]
    [InlineData(6, "100111110010111")]
    [InlineData(7, "100101010100000")]
    public void FormatBits_MatchesStandardTable(int mask, string expected)
    {
        Assert.Equal(Convert.ToInt32(expected, 2), QrCode.FormatBits(mask));
    }

    [Theory]
    [InlineData(7, 0x07C94)]
    [InlineData(8, 0x085BC)]
    [InlineData(9, 0x09A99)]
    [InlineData(10, 0x0A4D3)]
    public void VersionBits_MatchesStandardTable(int version, int expected)
    {
        Assert.Equal(expected, QrCode.VersionBits(version));
    }

    // ── 構造 ─────────────────────────────────────────────

    [Theory]
    [InlineData("A", 1)]
    [InlineData("http://192.168.10.105:8000/xK9mP2abcdEFGH1234_-yZ/", 4)]
    public void Encode_ChoosesSmallestVersion(string text, int expectedVersion)
    {
        var qr = QrCode.Encode(text)!;

        Assert.Equal(expectedVersion, qr.Version);
        Assert.Equal(expectedVersion * 4 + 17, qr.Size);
    }

    [Fact]
    public void Encode_TooLong_ReturnsNull()
    {
        // バージョン10-M のバイトモード上限は213バイト
        Assert.NotNull(QrCode.Encode(new string('x', 213)));
        Assert.Null(QrCode.Encode(new string('x', 214)));
    }

    [Fact]
    public void FinderPatterns_AndDarkModule_ArePresent()
    {
        var qr = QrCode.Encode("hello")!;
        var n = qr.Size;

        foreach (var (ox, oy) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            for (var y = 0; y < 7; y++)
            {
                for (var x = 0; x < 7; x++)
                {
                    var ring = Math.Max(Math.Abs(x - 3), Math.Abs(y - 3));
                    Assert.Equal(ring != 2, qr[ox + x, oy + y]);
                }
            }
        }
        Assert.True(qr[8, n - 8]);
    }

    [Fact]
    public void FormatInfo_InSymbol_MatchesChosenMask()
    {
        var qr = QrCode.Encode("http://10.0.0.5:8123/t/")!;
        var bits = QrCode.FormatBits(qr.Mask);

        // 右上の8ビットと左下の7ビット
        for (var i = 0; i < 8; i++)
            Assert.Equal(((bits >> i) & 1) != 0, qr[qr.Size - 1 - i, 8]);
        for (var i = 8; i < 15; i++)
            Assert.Equal(((bits >> i) & 1) != 0, qr[8, qr.Size - 15 + i]);
    }

    // ── 読み戻し ─────────────────────────────────────────

    [Theory]
    [InlineData("A", null)]
    [InlineData("http://192.168.10.105:8000/xK9mP2abcdEFGH1234_-yZ/", null)]
    [InlineData("http://[fe80::1234:5678:9abc:def0]:8000/xK9mP2abcdEFGH1234_-yZ/", null)]
    [InlineData("http://日本語ホスト.local:8000/t/", null)]
    [InlineData("hello", 0)]
    [InlineData("hello", 3)]
    [InlineData("hello", 7)]
    public void Decode_ReturnsOriginalText(string text, int? mask)
    {
        var qr = QrCode.Encode(text, mask)!;

        Assert.Equal(text, Decode(qr));
    }

    [Theory]
    [InlineData(150)]
    [InlineData(200)]
    public void Decode_MultiBlockVersions_ReturnsOriginalText(int length)
    {
        // バージョン8以上はブロックが複数（インターリーブあり）
        var text = string.Concat(Enumerable.Range(0, length).Select(i => (char)('a' + i % 26)));
        var qr = QrCode.Encode(text)!;

        Assert.True(qr.Version >= 8);
        Assert.Equal(text, Decode(qr));
    }

    [Fact]
    public void ToSvg_ContainsPathAndSize()
    {
        var svg = QrCode.Encode("hello")!.ToSvg(pixelSize: 120);

        Assert.StartsWith("<svg", svg);
        Assert.Contains("width=\"120\"", svg);
        Assert.Contains("<path", svg);
    }

    [Fact]
    public void ToConsoleLines_HalfHeight()
    {
        var qr = QrCode.Encode("hello")!;
        var lines = qr.ToConsoleLines(quietZone: 2).ToList();

        Assert.Equal((qr.Size + 4 + 1) / 2, lines.Count);
    }

    // ── 規格の手順で読み戻す（テスト用の簡易デコーダ） ──

    private static string Decode(QrCode qr)
    {
        var n = qr.Size;

        // 形式情報からマスク番号を読む（左上）
        var format = 0;
        for (var i = 0; i <= 5; i++) format |= (qr[8, i] ? 1 : 0) << i;
        format |= (qr[8, 7] ? 1 : 0) << 6;
        format |= (qr[8, 8] ? 1 : 0) << 7;
        format |= (qr[7, 8] ? 1 : 0) << 8;
        for (var i = 9; i < 15; i++) format |= (qr[14 - i, 8] ? 1 : 0) << i;
        var mask = Enumerable.Range(0, 8).Single(m => QrCode.FormatBits(m) == format);

        // 右下から2列ずつジグザグに読む
        var bits = new List<bool>();
        for (var right = n - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < n; vert++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var y = ((right + 1) & 2) == 0 ? n - 1 - vert : vert;
                    if (qr.IsFunctionModule(x, y)) continue;
                    var masked = mask switch
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
                    bits.Add(qr[x, y] ^ masked);
                }
            }
        }

        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < codewords.Length * 8; i++)
            if (bits[i]) codewords[i >> 3] |= (byte)(0x80 >> (i & 7));

        // デインターリーブ（誤り訂正レベルMのブロック構成）
        var (ec, b1, d1, b2, d2) = qr.Version switch
        {
            1 => (10, 1, 16, 0, 0), 2 => (16, 1, 28, 0, 0), 3 => (26, 1, 44, 0, 0),
            4 => (18, 2, 32, 0, 0), 5 => (24, 2, 43, 0, 0), 6 => (16, 4, 27, 0, 0),
            7 => (18, 4, 31, 0, 0), 8 => (22, 2, 38, 2, 39), 9 => (22, 3, 36, 2, 37),
            _ => (26, 4, 43, 1, 44),
        };
        var blocks = Enumerable.Range(0, b1 + b2).Select(i => new List<byte>()).ToList();
        var lengths = Enumerable.Range(0, b1 + b2).Select(i => i < b1 ? d1 : d2).ToList();
        var k = 0;
        for (var i = 0; i < Math.Max(d1, d2); i++)
            for (var b = 0; b < blocks.Count; b++)
                if (i < lengths[b]) blocks[b].Add(codewords[k++]);
        var ecBlocks = Enumerable.Range(0, blocks.Count).Select(i => new List<byte>()).ToList();
        for (var i = 0; i < ec; i++)
            for (var b = 0; b < blocks.Count; b++)
                ecBlocks[b].Add(codewords[k++]);

        // 各ブロックの誤り訂正コード語が正しいこと
        var generator = QrCode.ReedSolomonGenerator(ec);
        for (var b = 0; b < blocks.Count; b++)
            Assert.Equal(QrCode.ReedSolomonRemainder(blocks[b].ToArray(), generator), ecBlocks[b].ToArray());

        // バイトモードとして解釈
        var data = blocks.SelectMany(b => b).ToArray();
        var pos = 0;
        int Read(int len)
        {
            var v = 0;
            for (var i = 0; i < len; i++, pos++)
                v = (v << 1) | ((data[pos >> 3] >> (7 - (pos & 7))) & 1);
            return v;
        }
        Assert.Equal(0b0100, Read(4));
        var count = Read(qr.Version <= 9 ? 8 : 16);
        var bytes = Enumerable.Range(0, count).Select(_ => (byte)Read(8)).ToArray();
        return Encoding.UTF8.GetString(bytes);
    }
}
