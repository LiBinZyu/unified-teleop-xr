using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 高鲁棒性 Markdown 转 Unity TextMeshPro RichText 桥接工具
/// </summary>
public static class MarkdownToTmpConverter
{
    // 多行代码块 ```lang ... ```
    private static readonly Regex FencedCodeBlockRegex = new Regex(
        @"^```[^\n\r]*\r?\n([\s\S]*?)\r?\n```$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 行内代码 `code`
    private static readonly Regex InlineCodeRegex = new Regex(
        @"`([^`\r\n]+)`",
        RegexOptions.Compiled);

    // 标题 (# 到 ######)，支持行首可选空格
    private static readonly Regex HeaderRegex = new Regex(
        @"^[ \t]*(#{1,6})[ \t]+([^\r\n]+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 分割线 (---, ***, ___)
    private static readonly Regex HrRegex = new Regex(
        @"^[ \t]*([-*_])[ \t]*\1[ \t]*\1[ \t\1]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 引用行 (> quote)
    private static readonly Regex BlockquoteRegex = new Regex(
        @"^[ \t]*>[ \t]?(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 任务列表 checkbox: - [ ] or - [x]
    private static readonly Regex TaskListUncheckedRegex = new Regex(
        @"^[ \t]*[-*+][ \t]+\[[ ]\][ \t]+(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TaskListCheckedRegex = new Regex(
        @"^[ \t]*[-*+][ \t]+\[[xX]\][ \t]+(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 无序列表 (- / * / +)，支持缩进保留
    private static readonly Regex BulletListRegex = new Regex(
        @"^([ \t]*)[-*+][ \t]+(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 有序列表 (1. item)，保留原标号
    private static readonly Regex OrderedListRegex = new Regex(
        @"^([ \t]*)(\d+)\.[ \t]+(.*)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // 粗斜体 ***text*** 或 ___text___
    private static readonly Regex BoldItalicRegex1 = new Regex(
        @"\*\*\*([^\*\r\n]+?)\*\*\*",
        RegexOptions.Compiled);
    private static readonly Regex BoldItalicRegex2 = new Regex(
        @"___([^_\r\n]+?)___",
        RegexOptions.Compiled);

    // 粗体 **text** 或 __text__
    private static readonly Regex BoldRegex1 = new Regex(
        @"\*\*([^\*\r\n]+?)\*\*",
        RegexOptions.Compiled);
    private static readonly Regex BoldRegex2 = new Regex(
        @"(?<!\w)__([^_\r\n]+?)__(?!\w)",
        RegexOptions.Compiled);

    // 斜体 *text* 或 _text_（仅匹配单词边界外的下划线，避免变量名 foo_bar_baz 误触发）
    private static readonly Regex ItalicRegex1 = new Regex(
        @"(?<!\*)\*([^\*\r\n]+?)\*(?!\*)",
        RegexOptions.Compiled);
    private static readonly Regex ItalicRegex2 = new Regex(
        @"(?<![\w_])_([^_\r\n]+?)_(?![\w_])",
        RegexOptions.Compiled);

    // 删除线 ~~text~~
    private static readonly Regex StrikeRegex = new Regex(
        @"~~([^~\r\n]+?)~~",
        RegexOptions.Compiled);

    // 图片 ![alt](url) -> 转换为提示文本，防止破损
    private static readonly Regex ImageRegex = new Regex(
        @"!\[([^\]]*)\]\(([^)]+)\)",
        RegexOptions.Compiled);

    // 链接 [text](url)
    private static readonly Regex LinkRegex = new Regex(
        @"\[([^\]]+)\]\(([^)]+)\)",
        RegexOptions.Compiled);

    // 连续空行压缩 (超过2个换行压缩为2个换行)
    private static readonly Regex MultipleNewlinesRegex = new Regex(
        @"\n{3,}",
        RegexOptions.Compiled);

    public static string ConvertToRichText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        // 1. 标准化换行符
        string text = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2. 保护代码块和行内代码，提取到占位符池
        var placeholders = new List<string>();

        // 匹配三反引号代码块
        text = FencedCodeBlockRegex.Replace(text, match =>
        {
            string codeContent = EscapeTmpTags(match.Groups[1].Value.TrimEnd('\n'));
            string formatted = $"<indent=4%><color=#DFDBFF><mark=#11142D80><font=\"Courier\"> {codeContent} </font></mark></color></indent>";
            placeholders.Add(formatted);
            return $"\u001A{placeholders.Count - 1}\u001A";
        });

        text = InlineCodeRegex.Replace(text, match =>
        {
            string codeContent = EscapeTmpTags(match.Groups[1].Value);
            string formatted = $"<mark=#DFDBFF><color=#11142D> <b>{codeContent}</b> </color></mark>";
            placeholders.Add(formatted);
            return $"\u001A{placeholders.Count - 1}\u001A";
        });

        // 3. 转义普通文本中可能破坏 TMP 标签的 < 和 >（仅当它们不是用于换行或特殊排版时）
        // 注意：占位符使用的是 \u001A，不含 < 和 >
        text = EscapeTmpTags(text);

        // 4. 处理图片 ![alt](url) -> [🖼️ alt]
        text = ImageRegex.Replace(text, match =>
        {
            string alt = match.Groups[1].Value;
            return string.IsNullOrEmpty(alt) ? "[Image]" : $"[Image: {alt}]";
        });

        // 5. 处理链接 [text](url) -> 仅下划线
        text = LinkRegex.Replace(text, "<link=\"$2\"><u>$1</u></link>");

        // 6. 任务列表
        text = TaskListCheckedRegex.Replace(text, "  ☑ $1");
        text = TaskListUncheckedRegex.Replace(text, "  ☐ $1");

        // 7. 标题 (# 到 ######)
        text = HeaderRegex.Replace(text, match =>
        {
            int level = match.Groups[1].Value.Length;
            string content = match.Groups[2].Value.Trim();
            int sizePercent = 140 - (level - 1) * 10; // H1: 140%, H2: 130%, H3: 120%, ...
            if (sizePercent < 90) sizePercent = 90;
            return $"\n<size={sizePercent}%><b>{content}</b></size>\n";
        });

        // 8. 分割线 -> 柔和中灰 #616B7E
        text = HrRegex.Replace(text, "<color=#616B7E>──────────────────────────────</color>");

        // 9. 引用块 (> text) -> 辅助中灰 #616B7E
        text = BlockquoteRegex.Replace(text, "<indent=3%><color=#616B7E>│ <i>$1</i></color></indent>");

        // 10. 无序列表与缩进处理
        text = BulletListRegex.Replace(text, match =>
        {
            string indent = match.Groups[1].Value;
            string content = match.Groups[2].Value;
            int level = indent.Length / 2; // 2空格一级缩进
            string bullet = level % 2 == 0 ? "•" : "◦";
            string spaces = new string(' ', level * 2);
            return $"{spaces}  {bullet} {content}";
        });

        // 11. 有序列表对齐
        text = OrderedListRegex.Replace(text, match =>
        {
            string indent = match.Groups[1].Value;
            string num = match.Groups[2].Value;
            string content = match.Groups[3].Value;
            return $"{indent}  {num}. {content}";
        });

        // 12. 行内样式（粗体、斜体、删除线）
        // 粗斜体必须先匹配
        text = BoldItalicRegex1.Replace(text, "<b><i>$1</i></b>");
        text = BoldItalicRegex2.Replace(text, "<b><i>$1</i></b>");

        // 粗体
        text = BoldRegex1.Replace(text, "<b>$1</b>");
        text = BoldRegex2.Replace(text, "<b>$1</b>");

        // 斜体
        text = ItalicRegex1.Replace(text, "<i>$1</i>");
        text = ItalicRegex2.Replace(text, "<i>$1</i>");

        // 删除线
        text = StrikeRegex.Replace(text, "<s>$1</s>");

        // 13. 还原代码块与行内代码占位符
        for (int i = 0; i < placeholders.Count; i++)
        {
            text = text.Replace($"\u001A{i}\u001A", placeholders[i]);
        }

        // 14. 压缩过多空行并去除首尾空白
        text = MultipleNewlinesRegex.Replace(text, "\n\n");

        return text.Trim();
    }

    /// <summary>
    /// 转义文本中可能导致 TMP RichText 解析报错的未闭合标签符号
    /// </summary>
    private static string EscapeTmpTags(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // 替换 < 和 > 为 Unicode 类似字符或实体，防止 TMP 标签解析崩溃
        return input.Replace("<", "＜").Replace(">", "＞");
    }
}
