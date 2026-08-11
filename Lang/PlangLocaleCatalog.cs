using System.Collections.Generic;

namespace PolarisTools.Lang
{
    /// <summary>「添加语言」列表里的一项：语言代码 + 显示名 + 游戏自己用的两字母简称（列表里当徽标画）。</summary>
    internal sealed class PlangLocaleInfo
    {
        public PlangLocaleInfo(string code, string badge, string displayName, string note = null)
        {
            Code = code;
            Badge = badge;
            DisplayName = displayName;
            Note = note;
        }

        /// <summary>语言代码，等于 <c>PolarisAPI.Game.CurrentLocale</c>（游戏的 family key）的取值。</summary>
        public string Code { get; }

        /// <summary>游戏语言包里写的两字母简称（JP/EN/CN/TC/KR/TH），当徽标用。</summary>
        public string Badge { get; }

        public string DisplayName { get; }

        /// <summary>额外说明，比如默认语言那条。</summary>
        public string Note { get; }
    }

    /// <summary>
    /// 游戏自带的本地化 family 清单，「添加语言」对话框的选项就是这一份——比让用户凭记忆手打
    /// <c>zh-tc</c> 还是 <c>zh-tw</c> 靠谱得多。
    /// <para>
    /// 数据来自游戏目录 <c>AliceInCradle_Data/StreamingAssets/localization/___family_*.txt</c>
    /// 每个文件的首行（第 1 列 = family key，第 2 列 = 两字母简称，第 3 列 = 选项里显示的全名），
    /// 和 <c>PolarisAPI.Game.CurrentLocale</c>（直通 <c>XX.TX.getCurrentFamilyName()</c>）对得上。
    /// </para>
    /// <para>
    /// family 集合在游戏里不是固定枚举，而是扫 localization 目录动态建出来的，玩家装了第三方
    /// 语言包就会多出别的 key——所以对话框里除了这份清单还留了"自定义代码"的入口，这里只是把
    /// 常用的那几个变成点一下就行。
    /// </para>
    /// </summary>
    internal static class PlangLocaleCatalog
    {
        public static IReadOnlyList<PlangLocaleInfo> BuiltIn { get; } = new[]
        {
            new PlangLocaleInfo("_", "JP", "日本語", "游戏默认语言，family key 就是一个下划线"),
            new PlangLocaleInfo("en", "EN", "English", "没有匹配到系统语言时游戏用它兜底"),
            new PlangLocaleInfo("zh-cn", "CN", "简体中文"),
            new PlangLocaleInfo("zh-tc", "TC", "繁體中文", "注意是 zh-tc，不是 zh-tw"),
            new PlangLocaleInfo("ko-kr", "KR", "한국어"),
            new PlangLocaleInfo("th", "TH", "ไทย"),
        };

        /// <summary>已知代码的显示名，用户在自定义框里手打了个已知代码时顺手把名字补上。</summary>
        public static string DisplayNameFor(string code)
        {
            foreach (PlangLocaleInfo info in BuiltIn)
            {
                if (string.Equals(info.Code, code, System.StringComparison.OrdinalIgnoreCase))
                    return info.DisplayName;
            }

            return null;
        }
    }
}
