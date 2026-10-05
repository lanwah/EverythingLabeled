using System.Text;
using EverythingLabeled.Service.Services.Abstractions;
using TinyPinyin;

namespace EverythingLabeled.Service.Services.Tags;

public sealed class NameInitialsGenerator : INameInitialsGenerator
{
    public string FromName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (IsAsciiAlphanumeric(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (!IsChinese(c))
                continue;

            var pinyin = PinyinHelper.GetPinyin(c);
            if (string.IsNullOrEmpty(pinyin))
                continue;

            sb.Append(char.ToLowerInvariant(pinyin[0]));
        }

        return sb.ToString();
    }

    private static bool IsAsciiAlphanumeric(char c) =>
        c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static bool IsChinese(char c) => c is >= '\u4e00' and <= '\u9fff';
}
