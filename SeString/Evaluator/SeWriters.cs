using System.Text;

namespace SharpVision.SeStrings.Evaluator;

/// Port of ironworks `sestring::format::write::Write`. Style/color callbacks have
/// no-op defaults.
public interface ISeWriter
{
    void WriteStr(string str);
    void SetStyle(SeStyle style, bool enabled) { }
    void PushColor(ColorUsage usage) { }
    void PopColor(ColorUsage usage) { }
}

/// Renders to markdown exactly like xiv-data-oxidizer's `MarkdownWriter`:
/// bold/foreground-color -> "**", italic -> "*". (Colors are flattened to bold;
/// the asymmetric push/pop matches oxidizer.)
public sealed class MarkdownWriter : ISeWriter
{
    public readonly StringBuilder Buffer = new();

    public void WriteStr(string str) => Buffer.Append(str);

    public void SetStyle(SeStyle style, bool enabled)
    {
        switch (style)
        {
            case SeStyle.Bold: Buffer.Append("**"); break;
            case SeStyle.Italic: Buffer.Append('*'); break;
            // Outline/Shadow: no markdown equivalent.
        }
    }

    public void PushColor(ColorUsage usage)
    {
        if (usage == ColorUsage.Foreground)
            Buffer.Append("**");
    }

    public void PopColor(ColorUsage usage)
    {
        if (usage != ColorUsage.Foreground)
            Buffer.Append("**");
    }
}

/// Plain text collector used while evaluating an immediate SeString expression
/// (ironworks `EvaluationWriter`): styles/colors are ignored.
public sealed class PlainWriter : ISeWriter
{
    public readonly StringBuilder Buffer = new();
    public void WriteStr(string str) => Buffer.Append(str);
}
