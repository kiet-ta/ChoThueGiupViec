namespace CommonService.Domain.StateMachines;

/// <summary>Raised when code asks for a state change that the machine does not allow. Handlers map it to HTTP 409.</summary>
public sealed class InvalidStateTransitionException(string machine, string from, string to)
    : InvalidOperationException($"{machine}: transition {from} -> {to} is not allowed.")
{
    public string Machine { get; } = machine;
    public string From { get; } = from;
    public string To { get; } = to;
}

/// <summary>An explicit allow-list of transitions. Anything not listed is rejected.</summary>
public sealed class StateMachine<TState> where TState : struct, Enum
{
    private readonly string _name;
    private readonly Dictionary<TState, HashSet<TState>> _next = [];

    public StateMachine(string name, IEnumerable<(TState From, TState To)> transitions)
    {
        _name = name;
        foreach (var state in Enum.GetValues<TState>())
        {
            _next[state] = [];
        }

        foreach (var (from, to) in transitions)
        {
            _next[from].Add(to);
        }
    }

    public bool CanTransition(TState from, TState to) => _next[from].Contains(to);

    public IReadOnlyCollection<TState> NextStates(TState from) => _next[from];

    public bool IsTerminal(TState state) => _next[state].Count == 0;

    /// <summary>Returns <paramref name="to"/> when the transition is allowed, otherwise throws.</summary>
    public TState Require(TState from, TState to) =>
        CanTransition(from, to) ? to : throw new InvalidStateTransitionException(_name, from.ToString(), to.ToString());
}
