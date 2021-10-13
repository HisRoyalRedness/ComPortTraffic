using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gallagher.Utilities
{
    public static class Extensions
    {
        static readonly byte MAX_VALID_HEX_DIGIT = 15;

        public static string BinToHexString(this IEnumerable<byte> data, string delimiter = null)
        {
            if (data == null)
                return string.Empty;
            return string.Join(delimiter, data.Select(b => $"{b:X2}"));
        }

        public static IEnumerable<byte> HexStringToBin(this string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString.Trim()))
                yield break;

            byte lastChar = 0;
            var isFirstChar = true;
            foreach(var c in hexString
                .Select(c => c.HexCharToInt())
                .Where(c => c <= MAX_VALID_HEX_DIGIT))
            {
                if (isFirstChar)
                    lastChar = (byte)(c << 4);
                else
                    yield return (byte)(lastChar + c);
                isFirstChar = !isFirstChar;
            }
        }

        static byte HexCharToInt(this char c)
        {
            if (c >= '0' && c <= '9')
                return (byte)(c - '0');
            else if (c >= 'A' && c <= 'F')
                return (byte)(c - 'A' + 10);
            else if (c >= 'a' && c <= 'f')
                return (byte)(c - 'a' + 10);
            else
                return (byte)(MAX_VALID_HEX_DIGIT + 1);
        }

        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int chunksize)
        {
            while (source.Any())
            {
                yield return source.Take(chunksize);
                source = source.Skip(chunksize);
            }
        }
    }
}
