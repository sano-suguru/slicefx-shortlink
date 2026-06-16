using System.Security.Cryptography;

namespace ShortLink.Api.Infrastructure;

public static class ShortCode
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Length = 7;

    public static string Generate()
    {
        // Rejection sampling eliminates modulo bias: 256 % 62 = 8, so values 0-7 would
        // otherwise appear slightly more often. Values >= 248 are discarded and resampled.
        const int AlphabetLen = 62; // == Alphabet.Length; kept as literal so Max is compile-time constant
        const int Max = 256 - (256 % AlphabetLen); // 248
        var chars = new char[Length];
        Span<byte> buf = stackalloc byte[Length * 2]; // extra bytes to avoid multiple Fill calls in the common case
        var filled = 0;
        while (filled < Length)
        {
            RandomNumberGenerator.Fill(buf);
            for (var i = 0; i < buf.Length && filled < Length; i++)
            {
                if (buf[i] < Max)
                {
                    chars[filled++] = Alphabet[buf[i] % Alphabet.Length];
                }
            }
        }
        return new string(chars);
    }
}
