using System.Globalization;

namespace SharpVision.SeStrings.Evaluator;

/// Port of ironworks `sestring::format::value::Value`. All values are numeric or
/// string; `Unknown` is an explicit fallback for missing parameters.
public readonly struct SeValue
{
    public enum Kind { U32, Str, Unknown }

    public readonly Kind Type;
    private readonly uint _num;
    private readonly string? _str;

    private SeValue(Kind type, uint num, string? str)
    {
        Type = type;
        _num = num;
        _str = str;
    }

    public static SeValue Num(uint value) => new(Kind.U32, value, null);
    public static SeValue Str(string value) => new(Kind.Str, 0, value);
    public static readonly SeValue Unknown = new(Kind.Unknown, 0, null);
    public static readonly SeValue True = Num(1);
    public static readonly SeValue False = Num(0);

    public bool IsUnknown => Type == Kind.Unknown;

    public uint ToU32() => Type switch
    {
        Kind.U32 => _num,
        // SE's number parser is lenient; fall back to 0 on failure.
        Kind.Str => uint.TryParse(_str!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0,
        _ => 0,
    };

    public bool ToBool() => ToU32() != 0;

    public string ToStr() => Type switch
    {
        Kind.U32 => _num.ToString(CultureInfo.InvariantCulture),
        Kind.Str => _str!,
        _ => "UNKNOWN",
    };

    public SeValue Not() => ToU32() == 0 ? True : False;

    public static SeValue FromBool(bool value) => value ? True : False;
}

public enum Gender { Male, Female }

public readonly struct Player
{
    public readonly string Name;
    public readonly Gender Gender;
    public Player(string name, Gender gender) { Name = name; Gender = gender; }
}

public enum ColorUsage { Foreground, Edge, Shadow }

public enum SeStyle { Bold, Italic, Outline, Shadow }
