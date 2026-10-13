using System.Globalization;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace SharpVision.SeStrings.Evaluator;

/// Formatting state, mirroring ironworks' `format::State`.
public sealed class SeState
{
    public required SeInput Input;
    public required ISeWriter Writer;
    public uint Time = SeStringEvaluator.FfxivEpoch;
}

/// C# port of ironworks `sestring::format` — evaluates a Lumina SeString into a
/// writer, replicating parameter substitution, conditionals, number/style macros,
/// and the `UNKNOWN` fallback. Data-dependent macros (sheet/noun/sound/icon/...)
/// are no-ops, exactly as in ironworks.
public static class SeStringEvaluator
{
    public const uint FfxivEpoch = 1377590400; // 2013/08/27 08:00:00 GMT

    public static void Format(ReadOnlySeString sestring, SeState state)
    {
        foreach (var payload in sestring)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
                state.Writer.WriteStr(UnescapeText(payload.AsSpan().ToString()));
            else if (payload.Type != ReadOnlySePayloadType.Invalid)
                FormatMacro(payload, state);
        }
    }

    // Lumina renders text payloads in macro-string form, escaping '<' (macro
    // start) and '\\' with a backslash. ironworks writes the raw UTF-8 text, so
    // undo the escaping to match.
    private static string UnescapeText(string text)
    {
        if (text.IndexOf('\\') < 0)
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
                sb.Append(text[++i]);
            else
                sb.Append(text[i]);
        }
        return sb.ToString();
    }

    private static void FormatMacro(ReadOnlySePayload payload, SeState state)
    {
        var args = new Args(payload);
        switch (payload.MacroCode)
        {
            case MacroCode.SetResetTime: SetResetTime(args, state); break;
            case MacroCode.SetTime: SetTime(args, state); break;
            case MacroCode.If: If(args, state); break;
            case MacroCode.Switch: Switch(args, state); break;
            case MacroCode.PcName: PcName(args, state); break;
            case MacroCode.IfPcGender: IfPcGender(args, state); break;
            case MacroCode.IfPcName: IfPcName(args, state); break;
            case MacroCode.IfSelf: IfSelf(args, state); break;
            case MacroCode.NewLine: state.Writer.WriteStr("\n"); break;
            case (MacroCode)0x17: state.Writer.WriteStr("\n"); break; // Key (page separator)
            case MacroCode.Color: HandleColor(ColorUsage.Foreground, args, state); break;
            case MacroCode.EdgeColor: HandleColor(ColorUsage.Edge, args, state); break;
            case MacroCode.ShadowColor: HandleColor(ColorUsage.Shadow, args, state); break;
            case MacroCode.SoftHyphen: state.Writer.WriteStr("­"); break;
            case MacroCode.Bold: StyleToggle(SeStyle.Bold, args, state); break;
            case MacroCode.Italic: StyleToggle(SeStyle.Italic, args, state); break;
            case MacroCode.NonBreakingSpace: state.Writer.WriteStr(" "); break;
            case MacroCode.Hyphen: state.Writer.WriteStr("–"); break;
            case MacroCode.Num: Identity(args, state); break;
            case MacroCode.Hex: Hex(args, state); break;
            case MacroCode.Kilo: Kilo(args, state); break;
            case MacroCode.Byte: Byte(args, state); break;
            case MacroCode.Sec: Sec(args, state); break;
            case MacroCode.Float: Float(args, state); break;
            case MacroCode.String: Identity(args, state); break;
            case MacroCode.Caps: Caps(args, state); break;
            case MacroCode.Head: Head(args, state); break;
            case MacroCode.HeadAll: HeadAll(args, state); break;
            case MacroCode.Lower: Lower(args, state); break;
            case MacroCode.LowerHead: LowerHead(args, state); break;
            case (MacroCode)0x2C: Split(args, state); break; // Split (not named in older Lumina enums)
            case MacroCode.ColorType: HandleColorType(ColorUsage.Foreground, args, state); break;
            case MacroCode.EdgeColorType: HandleColorType(ColorUsage.Edge, args, state); break;
            case MacroCode.Ruby: Ruby(args, state); break;
            case MacroCode.Digit: Digit(args, state); break;
            case MacroCode.Ordinal: Ordinal(args, state); break;
            // No-op (match ironworks): Sheet, SheetSub, Sound, LevelPos, *Noun,
            // Icon, Wait, Link, Josa, Josaro, SwitchPlatform, and anything else.
            default: break;
        }
    }

    private static void Identity(Args args, SeState state)
    {
        if (args.TryNext(out var expr))
            FormatExpression(expr, state);
    }

    public static void FormatExpression(ReadOnlySeExpression expression, SeState state)
    {
        // Immediate SeString branches format directly into the current writer.
        if (expression.TryGetString(out var ses))
        {
            Format(ses, state);
            return;
        }

        state.Writer.WriteStr(EvaluateExpression(expression, state).ToStr());
    }

    // ---- expression evaluation (expression.rs) ----

    public static SeValue EvaluateExpression(ReadOnlySeExpression expression, SeState state)
    {
        if (expression.TryGetUInt(out uint u))
            return SeValue.Num(u);

        if (expression.TryGetString(out var ses))
            return EvaluateSeString(ses, state);

        if (expression.TryGetParameterExpression(out byte paramType, out var operand))
        {
            var et = (ExpressionType)paramType;
            return et switch
            {
                ExpressionType.LocalNumber => U32Param(state.Input.LocalParameter(EvaluateExpression(operand, state).ToU32())),
                ExpressionType.GlobalNumber => U32Param(state.Input.GlobalParameter(EvaluateExpression(operand, state).ToU32())),
                ExpressionType.LocalString => StrParam(state.Input.LocalParameter(EvaluateExpression(operand, state).ToU32())),
                ExpressionType.GlobalString => StrParam(state.Input.GlobalParameter(EvaluateExpression(operand, state).ToU32())),
                _ => SeValue.Unknown,
            };
        }

        if (expression.TryGetBinaryExpression(out byte binType, out var left, out var right))
        {
            var et = (ExpressionType)binType;
            var l = EvaluateExpression(left, state);
            var r = EvaluateExpression(right, state);
            return et switch
            {
                ExpressionType.GreaterThanOrEqualTo => Cmp((a, b) => a >= b, l, r),
                ExpressionType.GreaterThan => Cmp((a, b) => a > b, l, r),
                ExpressionType.LessThanOrEqualTo => Cmp((a, b) => a <= b, l, r),
                ExpressionType.LessThan => Cmp((a, b) => a < b, l, r),
                ExpressionType.Equal => Eq(l, r),
                ExpressionType.NotEqual => Eq(l, r).Not(),
                _ => SeValue.Unknown,
            };
        }

        if (expression.TryGetPlaceholderExpression(out byte phType))
        {
            var et = (ExpressionType)phType;
            return et switch
            {
                ExpressionType.Millisecond => Time(dt => (uint)dt.Millisecond, state),
                ExpressionType.Second => Time(dt => (uint)dt.Second, state),
                ExpressionType.Minute => Time(dt => (uint)dt.Minute, state),
                ExpressionType.Hour => Time(dt => (uint)dt.Hour, state),
                ExpressionType.Day => Time(dt => (uint)dt.Day, state),
                ExpressionType.Weekday => Time(dt => (uint)dt.DayOfWeek + 1, state),
                ExpressionType.Month => Time(dt => (uint)dt.Month, state),
                ExpressionType.Year => Time(dt => (uint)dt.Year, state),
                ExpressionType.StackColor => SeValue.Unknown,
                _ => SeValue.Unknown,
            };
        }

        return SeValue.Unknown;
    }

    private static SeValue EvaluateSeString(ReadOnlySeString sestring, SeState state)
    {
        var writer = new PlainWriter();
        var inner = new SeState { Input = state.Input, Writer = writer, Time = state.Time };
        Format(sestring, inner);
        return SeValue.Str(writer.Buffer.ToString());
    }

    private static SeValue U32Param(SeValue value) => value.IsUnknown ? SeValue.Unknown : SeValue.Num(value.ToU32());
    private static SeValue StrParam(SeValue value) => value.IsUnknown ? SeValue.Unknown : SeValue.Str(value.ToStr());

    private static SeValue Cmp(Func<uint, uint, bool> op, SeValue left, SeValue right)
    {
        // Unknown is treated as always-successful.
        if (left.IsUnknown || right.IsUnknown)
            return SeValue.True;
        return SeValue.FromBool(op(left.ToU32(), right.ToU32()));
    }

    private static SeValue Eq(SeValue left, SeValue right)
    {
        if (left.IsUnknown || right.IsUnknown)
            return SeValue.True;
        if (left.Type == SeValue.Kind.Str && right.Type == SeValue.Kind.Str)
            return SeValue.FromBool(left.ToStr() == right.ToStr());
        return SeValue.FromBool(left.ToU32() == right.ToU32());
    }

    private static SeValue Time(Func<DateTimeOffset, uint> get, SeState state)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(state.Time).UtcDateTime;
        return SeValue.Num(get(dt));
    }

    // ---- control flow (control_flow.rs, runtime.rs) ----

    private static void If(Args args, SeState state)
    {
        var condition = args.EvalValue(state);
        bool truthy = condition.IsUnknown || condition.ToBool();
        FormatBranch(args, truthy ? 0 : 1, state);
    }

    private static void Switch(Args args, SeState state)
    {
        var scrutinee = args.EvalValue(state);
        uint index = scrutinee.IsUnknown ? 1 : Math.Max(1u, scrutinee.ToU32());
        FormatBranch(args, (int)(index - 1), state);
    }

    private static void PcName(Args args, SeState state)
    {
        uint objectId = args.EvalU32(state);
        state.Writer.WriteStr(state.Input.GetPlayer(objectId).Name);
    }

    private static void IfPcGender(Args args, SeState state)
    {
        uint objectId = args.EvalU32(state);
        bool condition = state.Input.GetPlayer(objectId).Gender == Gender.Male;
        FormatBranch(args, condition ? 0 : 1, state);
    }

    private static void IfPcName(Args args, SeState state)
    {
        uint objectId = args.EvalU32(state);
        string name = args.EvalStr(state);
        bool condition = state.Input.GetPlayer(objectId).Name == name;
        FormatBranch(args, condition ? 0 : 1, state);
    }

    private static void IfSelf(Args args, SeState state)
    {
        var objectId = args.EvalValue(state);
        var playerId = state.Input.LocalPlayerId;
        bool condition = objectId.IsUnknown || playerId is null || objectId.ToU32() == playerId.Value;
        FormatBranch(args, condition ? 0 : 1, state);
    }

    private static void FormatBranch(Args args, int index, SeState state)
    {
        var branch = args.Nth(index);
        if (branch is { } expr)
            FormatExpression(expr, state);
    }

    // ---- style / color (style.rs) ----

    private static void StyleToggle(SeStyle style, Args args, SeState state)
    {
        bool enabled = args.EvalBool(state);
        state.Writer.SetStyle(style, enabled);
    }

    private static void HandleColorType(ColorUsage usage, Args args, SeState state)
    {
        uint id = args.EvalU32(state);
        if (id == 0)
            state.Writer.PopColor(usage);
        else
            state.Writer.PushColor(usage);
    }

    private static void HandleColor(ColorUsage usage, Args args, SeState state)
    {
        if (!args.TryNext(out var expr))
            return;
        // StackColor represents an immediate pop for this usage.
        if (expr.TryGetPlaceholderExpression(out byte ph) && (ExpressionType)ph == ExpressionType.StackColor)
        {
            state.Writer.PopColor(usage);
            return;
        }
        state.Writer.PushColor(usage);
    }

    // ---- number (number.rs) ----

    private static void Hex(Args args, SeState state) =>
        state.Writer.WriteStr(args.EvalU32(state).ToString("X", CultureInfo.InvariantCulture));

    private static void Kilo(Args args, SeState state)
    {
        uint value = args.EvalU32(state);
        string separator = args.EvalStr(state);
        state.Writer.WriteStr(value <= 999 ? value.ToString(CultureInfo.InvariantCulture) : FormatFloat(value, 1000, separator));
    }

    private static void Byte(Args args, SeState state)
    {
        uint value = args.EvalU32(state);
        string suffix = "";
        foreach (var next in new[] { "K", "M", "G", "T" })
        {
            if (value < 1024) break;
            value /= 1024;
            suffix = next;
        }
        state.Writer.WriteStr($"{value}{suffix}");
    }

    private static void Sec(Args args, SeState state) =>
        state.Writer.WriteStr(args.EvalU32(state).ToString("D2", CultureInfo.InvariantCulture));

    private static void Digit(Args args, SeState state)
    {
        uint value = args.EvalU32(state);
        uint length = args.EvalU32(state);
        state.Writer.WriteStr(value.ToString("D" + length.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }

    private static void Float(Args args, SeState state)
    {
        uint value = args.EvalU32(state);
        uint radix = args.EvalU32(state);
        string separator = args.EvalStr(state);
        state.Writer.WriteStr(FormatFloat(value, radix, separator));
    }

    private static void Ordinal(Args args, SeState state) =>
        // ironworks omits the english suffix; just the number.
        state.Writer.WriteStr(args.EvalU32(state).ToString(CultureInfo.InvariantCulture));

    private static string FormatFloat(uint value, uint radix, string separator)
    {
        if (radix == 0)
            radix = 1;
        uint left = value / radix;
        uint right = value % radix;
        int length = (int)Math.Floor(Math.Log10(radix));
        if (length < 0) length = 0;
        return $"{left}{separator}{right.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0')}";
    }

    // ---- text (text.rs) ----

    private static void Caps(Args args, SeState state) =>
        state.Writer.WriteStr(args.EvalStr(state).ToUpperInvariant());

    private static void Lower(Args args, SeState state) =>
        state.Writer.WriteStr(args.EvalStr(state).ToLowerInvariant());

    private static void Head(Args args, SeState state) =>
        state.Writer.WriteStr(HeadStr(args.EvalStr(state)));

    private static void HeadAll(Args args, SeState state)
    {
        var input = args.EvalStr(state);
        state.Writer.WriteStr(string.Join(" ", input.Split(' ').Select(HeadStr)));
    }

    private static void LowerHead(Args args, SeState state)
    {
        var input = args.EvalStr(state);
        if (input.Length == 0)
        {
            state.Writer.WriteStr(input);
            return;
        }
        state.Writer.WriteStr(char.ToLowerInvariant(input[0]) + input.Substring(1));
    }

    private static void Ruby(Args args, SeState state)
    {
        string str = args.EvalStr(state);
        string pron = args.EvalStr(state);
        state.Writer.WriteStr($"{str} ({pron})");
    }

    private static void Split(Args args, SeState state)
    {
        string str = args.EvalStr(state);
        string pattern = args.EvalStr(state);
        uint index = args.EvalU32(state);
        var parts = str.Split(pattern);
        state.Writer.WriteStr(index < parts.Length ? parts[index] : string.Empty);
    }

    private static string HeadStr(string input) =>
        input.Length == 0 ? input : char.ToUpperInvariant(input[0]) + input.Substring(1);

    // ---- time (time.rs) ----

    private static void SetTime(Args args, SeState state)
    {
        var value = args.EvalValue(state);
        state.Time = value.IsUnknown ? FfxivEpoch : value.ToU32();
    }

    private static void SetResetTime(Args args, SeState state)
    {
        uint targetHour = args.EvalU32(state);
        uint? targetWeekday = args.TryEvalU32(state, out var wd) ? wd : null;

        var dt = DateTimeOffset.FromUnixTimeSeconds(state.Time).UtcDateTime;
        int dayOffset = 0;

        if (targetWeekday is { } tw)
        {
            int currentWeekday = (int)dt.DayOfWeek; // days from sunday
            dayOffset += (int)tw - currentWeekday;
            if (dayOffset < 0)
                dayOffset += 7;
        }

        if (targetHour < (uint)dt.Hour && dayOffset <= 0)
            dayOffset += 1;

        dt = dt.AddDays(dayOffset);
        dt = dt.Date.AddHours(targetHour);
        state.Time = (uint)new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    /// Iterator over a payload's expressions, mirroring ironworks' `Arguments`.
    private sealed class Args
    {
        private readonly List<ReadOnlySeExpression> _exprs = new();
        private int _pos;

        public Args(ReadOnlySePayload payload)
        {
            if (payload is ICollection<ReadOnlySeExpression> collection)
                foreach (var e in collection)
                    _exprs.Add(e);
        }

        public bool TryNext(out ReadOnlySeExpression expr)
        {
            if (_pos < _exprs.Count)
            {
                expr = _exprs[_pos++];
                return true;
            }
            expr = default;
            return false;
        }

        // Skips `index` expressions and returns the next, advancing past it.
        public ReadOnlySeExpression? Nth(int index)
        {
            int target = _pos + index;
            if (index < 0 || target >= _exprs.Count)
            {
                _pos = _exprs.Count;
                return null;
            }
            _pos = target + 1;
            return _exprs[target];
        }

        public SeValue EvalValue(SeState state) =>
            TryNext(out var e) ? EvaluateExpression(e, state) : SeValue.Unknown;

        public uint EvalU32(SeState state) => EvalValue(state).ToU32();
        public bool EvalBool(SeState state) => EvalValue(state).ToBool();
        public string EvalStr(SeState state) => EvalValue(state).ToStr();

        public bool TryEvalU32(SeState state, out uint value)
        {
            if (TryNext(out var e))
            {
                value = EvaluateExpression(e, state).ToU32();
                return true;
            }
            value = 0;
            return false;
        }
    }
}
