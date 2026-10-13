namespace SharpVision.SeStrings.Evaluator;

/// Port of ironworks `sestring::format::input::Input`. Supplies the data a string
/// references (parameters, players). Unset values fall back the same way ironworks
/// does, so output matches.
public sealed class SeInput
{
    private readonly Dictionary<uint, Player> _players = new();
    private uint? _localPlayer;
    private readonly Dictionary<uint, SeValue> _local = new();
    private readonly Dictionary<uint, SeValue> _global = new();

    public void AddGlobalParameter(uint id, SeValue value) => _global[id] = value;
    public void AddLocalParameter(uint id, SeValue value) => _local[id] = value;
    public void AddPlayer(uint id, Player player) => _players[id] = player;
    public void SetLocalPlayerId(uint id) => _localPlayer = id;

    public Player GetPlayer(uint id) =>
        _players.TryGetValue(id, out var p) ? p : new Player("Firstname Lastname", Gender.Male);

    public uint? LocalPlayerId => _localPlayer;

    public SeValue LocalParameter(uint id) => _local.TryGetValue(id, out var v) ? v : SeValue.Unknown;
    public SeValue GlobalParameter(uint id) => _global.TryGetValue(id, out var v) ? v : SeValue.Unknown;

    /// Mirrors the Input xiv-data-oxidizer uses when extracting CSV: only global
    /// parameter 1 (the player name) is provided; everything else falls back.
    public static SeInput Oxidizer()
    {
        var input = new SeInput();
        input.AddGlobalParameter(1, SeValue.Str("Player Player"));
        return input;
    }
}
