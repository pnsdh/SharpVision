using Lumina.Text.ReadOnly;
using SharpVision.SeStrings.Evaluator;

namespace SharpVision.SeStrings;

/// Renders a SeString to markdown text for CSV cells, matching
/// xiv-data-oxidizer's output via a faithful C# port of ironworks'
/// `sestring::format` evaluator (see <see cref="SeStringEvaluator"/>).
public static class SeStringMarkdown
{
    // The Input is read-only after construction and matches what oxidizer feeds
    // ironworks (global parameter 1 = "Player Player"); reuse it across cells.
    private static readonly SeInput Input = SeInput.Oxidizer();

    public static string Format(ReadOnlySeString value)
    {
        var writer = new MarkdownWriter();
        var state = new SeState { Input = Input, Writer = writer };
        SeStringEvaluator.Format(value, state);

        var result = writer.Buffer.ToString();
        if (result.IndexOf('\r') >= 0)
            result = result.Replace("\r\n", "\n").Replace("\r", "\n");
        return result;
    }
}
