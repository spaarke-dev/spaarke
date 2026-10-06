using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Spaarke.ArchTests;

/// <summary>
/// The compiled PATH counterpart of <see cref="IlCallScan"/>: whether every path through one method's compiled body that
/// makes an OPENING call (a POA share write's send) goes on, before the method completes, to make the DISCHARGING call (the
/// share-write notification) — only after the opening call's task has completed, with the same key arguments, and awaiting
/// the discharge's own task.
/// </summary>
/// <remarks>
/// <para><b>Why a path analysis.</b> A reference scan answers "which methods make the send"; it cannot answer "does every
/// path from that send reach the notification". Task 132 verifier seed N1 — an early
/// <c>if (principal.Kind == Team) { await _dataverse.GrantAccessAsync(…); return; }</c> before the <c>try</c> whose
/// <c>finally</c> evicted — wrote a team share with no eviction from exactly the method a reference pin allowed. Only the
/// paths through the body can tell those apart. (Round 55 moved the notification into the client's own share-write
/// sender; the guard now analyses that method, with the opening keyed by its own parameters —
/// <see cref="Rule.OpeningKeysFromSource"/>.)</para>
///
/// <para><b>The model.</b> An abstract interpretation of the body's IL, explored path by path to a fixed point:</para>
/// <list type="bullet">
/// <item><b>Async methods are analysed as the compiler built them</b> — the state machine's <c>MoveNext</c>. The state
/// field and the dispatch local are tracked as constants, so the dispatch branches are decided exactly; a <c>ret</c> with a
/// non-negative state is a SUSPENSION (the analysis resumes at <c>MoveNext</c>'s entry in that state, keeping the state
/// machine's fields — the parked awaiter included — and the obligation); a <c>ret</c> in state -1 or -2 is COMPLETION.
/// A <c>ret</c> whose state the analysis lost is reported, not guessed.</item>
/// <item><b>Exceptions</b> leave every instruction that can raise one — every call except the await plumbing named in
/// <see cref="IsAwaitPlumbing"/> (which fails only by running out of memory), <c>throw</c>, <c>rethrow</c>, a checked
/// conversion, cast, array access or division, a static field, a field reached through anything but <c>this</c> or the
/// state machine's owner — to each enclosing handler, innermost first: a <c>catch (object)</c> or <c>catch (Exception)</c>
/// stops the search (a C# assembly wraps non-Exception throws), a filter is assumed to accept AND to decline, a
/// <c>finally</c> / <c>fault</c> runs and the search continues after it. An exception no handler stops COMPLETES the
/// method. <c>leave</c> runs every <c>finally</c> it crosses, in order.</item>
/// <item><b>The obligation</b> moves None → Issued (the opening call returned its task) → Owed (that task's awaiter
/// returned or threw from <c>GetResult</c>; or the opening call itself threw) → Discharging (the discharging call was made
/// with key arguments EQUAL to the opening call's — the same hoisted parameter, never re-assigned on the path) → None (the
/// discharge's awaiter returned or threw). Awaiters are followed through <c>ConfigureAwait</c>, <c>GetAwaiter</c>, locals,
/// the state machine's awaiter fields and suspension. A discharge while Issued (the write still in flight), a discharge
/// with other keys, a second opening while one is owed, and a completion in any phase but None are violations.</item>
/// <item><b>Values</b> are constants, <c>this</c>, the state machine's owner (never null), a parameter (by name — a hoisted
/// one in a state machine), a tracked awaitable / awaiter, an address of a local or of one of the state machine's fields,
/// or unknown. A call that takes a local's or field's address by mutable reference forgets what it held. An unknown
/// condition takes both branches.</item>
/// </list>
///
/// <para><b>Fails loud.</b> An unrecognised state-machine prologue, an <c>endfinally</c> the analysis did not enter, a
/// <c>calli</c> / <c>jmp</c>, a lost state at <c>ret</c>, or a path explosion throws or reports — an analysis that
/// silently dropped paths would under-report, which for a guard is worse than none.</para>
///
/// <para><b>Out of its reach by construction</b>: what the discharging call DOES with its arguments (that is the
/// discharging method's own analysis, or a behaviour test's); out-of-memory, stack overflow and process death.</para>
/// </remarks>
internal static class IlPathScan
{
    /// <summary>What one analysis checks.</summary>
    internal sealed class Rule
    {
        /// <summary>A call that opens the obligation (a POA share write).</summary>
        public required Func<MethodBase, bool> Opens { get; init; }

        /// <summary>A call that discharges it (the eviction).</summary>
        public required Func<MethodBase, bool> Discharges { get; init; }

        /// <summary>
        /// Parameter names carried by BOTH the opening and the discharging target (and, with <see cref="OwedAtEntry"/>, by the
        /// analysed method): the discharge must pass the very values the opening call was given.
        /// </summary>
        public required IReadOnlyList<string> KeyParameters { get; init; }

        /// <summary>
        /// The obligation is owed from entry, keyed by the analysed method's OWN parameters of those names — for a method
        /// whose whole job is the discharge (the client's notification helper).
        /// </summary>
        public bool OwedAtEntry { get; init; }

        /// <summary>
        /// An opening call is keyed by the analysed method's OWN parameters of the <see cref="KeyParameters"/> names, not by
        /// the opening target's — for an opening that carries no such parameter (an HTTP send whose request the method
        /// built for that record).
        /// </summary>
        public bool OpeningKeysFromSource { get; init; }

        /// <summary>What the opening side is called in a violation ("the POA write").</summary>
        public string Opening { get; init; } = "the write";

        /// <summary>What the discharge is called in a violation ("the eviction").</summary>
        public string Discharge { get; init; } = "the discharge";
    }

    /// <summary>The violations found, and the offsets of the opening and discharging calls the analysis reached.</summary>
    internal sealed record Result(IReadOnlyList<string> Violations, IReadOnlyList<int> OpeningCalls, IReadOnlyList<int> DischargingCalls);

    private enum Phase
    {
        None,
        Issued,
        Owed,
        Discharging,
    }

    // ── abstract values ──────────────────────────────────────────────────────────────────────────────────────

    private abstract record V;

    private sealed record Unknown : V
    {
        internal static readonly Unknown Value = new();

        public override string ToString() => "?";
    }

    private sealed record Const(int Value) : V;

    /// <summary><c>this</c> of the analysed body — the state machine, in a <c>MoveNext</c>.</summary>
    private sealed record This : V
    {
        internal static readonly This Value = new();
    }

    /// <summary>A state machine's <c>&lt;&gt;4__this</c>: the instance the async method was called on, never null.</summary>
    private sealed record Owner : V
    {
        internal static readonly Owner Value = new();
    }

    private sealed record Param(string Name) : V
    {
        public override string ToString() => Name;
    }

    /// <summary>The opening (or discharging) call's task, its configured awaitable, or its awaiter (stage 0, 1, 2).</summary>
    private sealed record Awaited(bool Opening, int Stage) : V;

    private sealed record LocalAddress(int Index) : V;

    private sealed record FieldAddress(string Name) : V;

    // ── finally continuations ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A <c>finally</c> entered by a <c>leave</c>: when it ends, run the remaining crossed ones, then go to the target.</summary>
    private sealed record LeaveCont(int Clause, int Target, ImmutableList<int> Remaining)
    {
        public override string ToString() => $"L{Clause}>{Target}[{string.Join(',', Remaining)}]";
    }

    /// <summary>A <c>finally</c> / <c>fault</c> entered by an exception: when it ends, continue the handler search.</summary>
    private sealed record UnwindCont(int Clause, int Site, int NextPosition)
    {
        public override string ToString() => $"U{Clause}>{Site}@{NextPosition}";
    }

    private sealed record State(
        int Pc,
        ImmutableList<V> Stack,
        ImmutableSortedDictionary<int, V> Locals,
        ImmutableSortedDictionary<string, V> Fields,
        ImmutableSortedDictionary<int, V> Args,
        Phase Phase,
        ImmutableList<V> Keys,
        int OpenedAt,
        ImmutableList<object> Conts)
    {
        internal string Key() =>
            $"{Pc}|{string.Join(",", Stack)}|{string.Join(",", Locals.Select(l => $"{l.Key}={l.Value}"))}|"
            + $"{string.Join(",", Fields.Select(f => $"{f.Key}={f.Value}"))}|{string.Join(",", Args.Select(a => $"{a.Key}={a.Value}"))}|"
            + $"{Phase}|{string.Join(",", Keys)}|{OpenedAt}|{string.Join(",", Conts)}";

        internal State Push(V value) => this with { Stack = Stack.Add(value) };
    }

    private const string StateField = "<>1__state";
    private const string OwnerField = "<>4__this";
    private const int MaxStates = 250_000;

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Analyses the compiled body of <paramref name="method"/> (its state machine's <c>MoveNext</c> when it is async).</summary>
    internal static Result Analyse(MethodInfo method, Rule rule) => new Analysis(method, rule).Run();

    private sealed class Analysis
    {
        private readonly MethodInfo _source;
        private readonly Rule _rule;
        private readonly MethodBase _body;
        private readonly bool _isStateMachine;
        private readonly int _stateLocal = -1;
        private readonly IReadOnlyList<IlCallScan.IlInstruction> _instructions;
        private readonly Dictionary<int, int> _indexAt;
        private readonly IReadOnlyList<ExceptionHandlingClause> _clauses;
        private readonly HashSet<string> _sourceParameters;
        private readonly ParameterInfo[] _bodyParameters;
        private readonly HashSet<string> _violations = new(StringComparer.Ordinal);
        private readonly SortedSet<int> _openings = new();
        private readonly SortedSet<int> _discharges = new();

        internal Analysis(MethodInfo source, Rule rule)
        {
            _source = source;
            _rule = rule;
            _body = IlCallScan.CompiledBody(source);
            _isStateMachine = !ReferenceEquals(_body, source);
            _instructions = IlCallScan.Instructions(_body);
            _indexAt = _instructions.Select((ins, index) => (ins.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
            _clauses = _body.GetMethodBody()?.ExceptionHandlingClauses.ToList() ?? new List<ExceptionHandlingClause>();
            _sourceParameters = source.GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);
            _bodyParameters = _body.GetParameters();

            if (_isStateMachine)
            {
                // Roslyn's MoveNext prologue: ldarg.0; ldfld <>1__state; stloc.N — N is the dispatch local.
                if (_instructions.Count < 3
                    || _instructions[0].OpCode != OpCodes.Ldarg_0
                    || _instructions[1].OpCode != OpCodes.Ldfld
                    || ResolveField(_instructions[1].Operand).Name != StateField
                    || LocalIndex(_instructions[2], store: true) is not { } local)
                {
                    throw new InvalidOperationException(
                        $"{IlCallScan.Describe(_body)}: unrecognised state-machine prologue — the path analysis cannot decide its dispatch.");
                }

                _stateLocal = local;
            }

            if (rule.OwedAtEntry || rule.OpeningKeysFromSource)
            {
                var missing = rule.KeyParameters.Where(k => !_sourceParameters.Contains(k)).ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{IlCallScan.Describe(source)} has no parameter {string.Join(", ", missing)} to key its obligation by.");
                }
            }
        }

        internal Result Run()
        {
            var initial = new State(
                Pc: 0,
                Stack: ImmutableList<V>.Empty,
                Locals: ImmutableSortedDictionary<int, V>.Empty,
                Fields: _isStateMachine
                    ? ImmutableSortedDictionary<string, V>.Empty.Add(StateField, new Const(-1))
                    : ImmutableSortedDictionary<string, V>.Empty,
                Args: ImmutableSortedDictionary<int, V>.Empty,
                Phase: _rule.OwedAtEntry ? Phase.Owed : Phase.None,
                Keys: _rule.OwedAtEntry
                    ? _rule.KeyParameters.Select(k => (V)new Param(k)).ToImmutableList()
                    : ImmutableList<V>.Empty,
                OpenedAt: -1,
                Conts: ImmutableList<object>.Empty);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<State>();
            pending.Push(initial);
            while (pending.Count > 0)
            {
                var state = pending.Pop();
                if (!seen.Add(state.Key()))
                {
                    continue;
                }

                if (seen.Count > MaxStates)
                {
                    throw new InvalidOperationException(
                        $"{IlCallScan.Describe(_body)}: the path analysis exceeded {MaxStates} states — it would not finish, so it reports instead of guessing.");
                }

                foreach (var next in Step(state))
                {
                    pending.Push(next);
                }
            }

            return new Result(_violations.OrderBy(v => v, StringComparer.Ordinal).ToList(), _openings.ToList(), _discharges.ToList());
        }

        // ── one instruction ──────────────────────────────────────────────────────────────────────────────

        private IEnumerable<State> Step(State s)
        {
            if (!_indexAt.TryGetValue(s.Pc, out var index))
            {
                throw new InvalidOperationException($"{IlCallScan.Describe(_body)}: a path reached IL_{s.Pc:X4}, which is not an instruction.");
            }

            var ins = _instructions[index];
            var op = ins.OpCode;
            var at = s with { Pc = ins.Next };

            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
            {
                return Call(s, ins);
            }

            if (op == OpCodes.Calli || op == OpCodes.Jmp)
            {
                throw new InvalidOperationException($"{IlCallScan.Describe(_body)}: IL_{ins.Offset:X4} {op.Name} is not modelled by the path analysis.");
            }

            if (op == OpCodes.Ret)
            {
                return Return(s, ins);
            }

            if (op == OpCodes.Throw || op == OpCodes.Rethrow)
            {
                return Throw(s with { Stack = ImmutableList<V>.Empty }, ins.Offset, 0);
            }

            if (op == OpCodes.Leave || op == OpCodes.Leave_S)
            {
                return new[] { Leave(s with { Stack = ImmutableList<V>.Empty }, ins.Offset, ins.Operand) };
            }

            if (op == OpCodes.Endfinally)
            {
                return EndFinally(s with { Stack = ImmutableList<V>.Empty }, ins.Offset);
            }

            if (op == OpCodes.Endfilter)
            {
                return Array.Empty<State>(); // both outcomes were taken when the filter was entered
            }

            if (op == OpCodes.Br || op == OpCodes.Br_S)
            {
                return new[] { s with { Pc = ins.Operand } };
            }

            if (op.FlowControl == FlowControl.Cond_Branch)
            {
                return Branch(s, ins);
            }

            // Straight-line instructions: the value effect, plus an exception edge where one can arise.
            var (after, mayThrow) = Effect(at, ins);
            var successors = new List<State> { after };
            if (mayThrow)
            {
                successors.AddRange(Throw(s with { Stack = ImmutableList<V>.Empty }, ins.Offset, 0));
            }

            return successors;
        }

        private (State After, bool MayThrow) Effect(State s, IlCallScan.IlInstruction ins)
        {
            var op = ins.OpCode;

            if (LocalIndex(ins, store: false) is { } load)
            {
                return (s.Push(s.Locals.GetValueOrDefault(load, Unknown.Value)), false);
            }

            if (LocalIndex(ins, store: true) is { } store)
            {
                var (rest, value) = Pop(s);
                return (rest with { Locals = rest.Locals.SetItem(store, store == _stateLocal ? value : Widen(value)) }, false);
            }

            if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
            {
                return (s.Push(new LocalAddress(ins.Operand)), false);
            }

            if (ArgIndex(ins) is { } arg)
            {
                return (s.Push(ArgValue(s, arg)), false);
            }

            if (op == OpCodes.Starg || op == OpCodes.Starg_S)
            {
                var (rest, value) = Pop(s);
                return (rest with { Args = rest.Args.SetItem(ins.Operand, Widen(value)) }, false);
            }

            if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
            {
                // The argument's address escapes: whatever it held is no longer known.
                var forgotten = s with { Args = s.Args.SetItem(ins.Operand, Unknown.Value) };
                return (forgotten.Push(Unknown.Value), false);
            }

            if (IntConstant(ins) is { } constant)
            {
                return (s.Push(new Const(constant)), false);
            }

            if (op == OpCodes.Dup)
            {
                return (s.Push(s.Stack[^1]), false);
            }

            if (op == OpCodes.Ldfld || op == OpCodes.Ldflda)
            {
                var (rest, receiver) = Pop(s);
                var field = ResolveField(ins.Operand);
                if (receiver is This)
                {
                    return (rest.Push(op == OpCodes.Ldflda ? new FieldAddress(field.Name) : FieldValue(rest, field)), false);
                }

                return (rest.Push(Unknown.Value), receiver is not (Owner or LocalAddress or FieldAddress));
            }

            if (op == OpCodes.Stfld)
            {
                var (rest, value) = Pop(s);
                (rest, var receiver) = Pop(rest);
                var field = ResolveField(ins.Operand);
                if (receiver is This)
                {
                    return (rest with { Fields = rest.Fields.SetItem(field.Name, field.Name == StateField ? value : Widen(value)) }, false);
                }

                return (rest, receiver is not (Owner or LocalAddress or FieldAddress));
            }

            if (op == OpCodes.Initobj)
            {
                var (rest, address) = Pop(s);
                return (Forget(rest, address), false);
            }

            if (op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un
                || op == OpCodes.Clt || op == OpCodes.Clt_Un)
            {
                var (rest, right) = Pop(s);
                (rest, var left) = Pop(rest);
                if (left is Const l && right is Const r)
                {
                    var value = op == OpCodes.Add ? l.Value + r.Value
                        : op == OpCodes.Sub ? l.Value - r.Value
                        : op == OpCodes.Ceq ? (l.Value == r.Value ? 1 : 0)
                        : op == OpCodes.Cgt ? (l.Value > r.Value ? 1 : 0)
                        : op == OpCodes.Cgt_Un ? ((uint)l.Value > (uint)r.Value ? 1 : 0)
                        : op == OpCodes.Clt ? (l.Value < r.Value ? 1 : 0)
                        : ((uint)l.Value < (uint)r.Value ? 1 : 0);
                    return (rest.Push(new Const(value)), false);
                }

                return (rest.Push(Unknown.Value), false);
            }

            // Anything else: its stack effect, unknown results.
            var popped = s;
            for (var i = 0; i < Pops(op); i++)
            {
                (popped, _) = Pop(popped);
            }

            for (var i = 0; i < Pushes(op); i++)
            {
                popped = popped.Push(Unknown.Value);
            }

            return (popped, MayThrow(op));
        }

        // ── calls: the obligation's transitions ──────────────────────────────────────────────────────────

        private IEnumerable<State> Call(State s, IlCallScan.IlInstruction ins)
        {
            var target = IlCallScan.Resolve(_body, ins.Operand, (module, token, ta, ma) => module.ResolveMethod(token, ta, ma))
                         ?? throw new InvalidOperationException($"{IlCallScan.Describe(_body)}: unresolvable call at IL_{ins.Offset:X4}.");
            var parameters = target.GetParameters();
            var isNewobj = ins.OpCode == OpCodes.Newobj;
            var hasReceiver = !isNewobj && !target.IsStatic;
            var count = parameters.Length + (hasReceiver ? 1 : 0);

            var rest = s with { Pc = ins.Next };
            var args = new V[count];
            for (var i = count - 1; i >= 0; i--)
            {
                (rest, args[i]) = Pop(rest);
            }

            var returns = isNewobj || (target is MethodInfo mi && mi.ReturnType != typeof(void));
            var receiver = hasReceiver ? Resolve(rest, args[0]) : null;
            var plumbing = IsAwaitPlumbing(target);

            // A call that may write through an address it was given forgets what that address held.
            if (!plumbing && target.Name != "GetResult")
            {
                for (var i = 0; i < count; i++)
                {
                    if (args[i] is LocalAddress or FieldAddress && WritesThrough(target, parameters, hasReceiver, i))
                    {
                        rest = Forget(rest, args[i]);
                    }
                }
            }

            V result = isNewobj ? Owner.Value : Unknown.Value;
            State normal;
            State? thrown = null;

            if (_rule.Opens(target))
            {
                _openings.Add(ins.Offset);
                if (s.Phase != Phase.None)
                {
                    Report(ins.Offset, s, $"a second call to {_rule.Opening} while {_rule.Discharge} of the first is still owed");
                    return Array.Empty<State>();
                }

                var keys = _rule.OpeningKeysFromSource
                    ? _rule.KeyParameters.Select(k => (V)new Param(k)).ToImmutableList()
                    : KeyValues(target, parameters, hasReceiver, args);
                var awaitable = target is MethodInfo m && IsAwaitable(m.ReturnType);
                normal = rest with { Phase = awaitable ? Phase.Issued : Phase.Owed, Keys = keys, OpenedAt = ins.Offset };
                result = awaitable ? new Awaited(Opening: true, Stage: 0) : Unknown.Value;

                // A write that throws may still have committed: nothing to await, the eviction is owed.
                thrown = rest with { Phase = Phase.Owed, Keys = keys, OpenedAt = ins.Offset };
            }
            else if (_rule.Discharges(target))
            {
                _discharges.Add(ins.Offset);
                switch (s.Phase)
                {
                    case Phase.Issued:
                        Report(ins.Offset, s, $"{_rule.Discharge} runs before {_rule.Opening}'s task has completed ({_rule.Opening} is not awaited first)");
                        return Array.Empty<State>();
                    case Phase.Owed:
                        var keys = KeyValues(target, parameters, hasReceiver, args);
                        if (!SameKeys(s.Keys, keys))
                        {
                            Report(ins.Offset, s, $"{_rule.Discharge} is given ({string.Join(", ", keys)}) where {_rule.Opening} was given ({string.Join(", ", s.Keys)})");
                            return Array.Empty<State>();
                        }

                        var awaitable = target is MethodInfo m && IsAwaitable(m.ReturnType);
                        normal = rest with { Phase = awaitable ? Phase.Discharging : Phase.None, Keys = awaitable ? s.Keys : ImmutableList<V>.Empty };
                        result = awaitable ? new Awaited(Opening: false, Stage: 0) : Unknown.Value;
                        thrown = rest with { Phase = Phase.None, Keys = ImmutableList<V>.Empty }; // the attempt is over
                        break;
                    default:
                        normal = rest; // nothing owed: a discharge with no write before it changes nothing
                        break;
                }
            }
            else if (receiver is Awaited awaited && target.Name is "ConfigureAwait" && awaited.Stage == 0)
            {
                normal = rest;
                result = awaited with { Stage = 1 };
            }
            else if (receiver is Awaited awaiting && target.Name is "GetAwaiter" && awaiting.Stage <= 1)
            {
                normal = rest;
                result = awaiting with { Stage = 2 };
            }
            else if (receiver is Awaited { Stage: 2 } awaiter && target.Name is "GetResult")
            {
                // The awaited operation has completed — returned or thrown — whichever edge leaves GetResult.
                normal = awaiter.Opening && s.Phase == Phase.Issued
                    ? rest with { Phase = Phase.Owed }
                    : !awaiter.Opening && s.Phase == Phase.Discharging
                        ? rest with { Phase = Phase.None, Keys = ImmutableList<V>.Empty }
                        : rest;
                thrown = normal;
            }
            else
            {
                normal = rest;
            }

            var successors = new List<State> { returns ? normal.Push(result) : normal };
            if (!plumbing)
            {
                successors.AddRange(Throw((thrown ?? normal) with { Stack = ImmutableList<V>.Empty }, ins.Offset, 0));
            }

            return successors;
        }

        private ImmutableList<V> KeyValues(MethodBase target, ParameterInfo[] parameters, bool hasReceiver, V[] args) =>
            _rule.KeyParameters
                .Select(name =>
                {
                    var position = Array.FindIndex(parameters, p => p.Name == name);
                    if (position < 0)
                    {
                        throw new InvalidOperationException(
                            $"{IlCallScan.Describe(target)} has no parameter '{name}' — the rule's key cannot be matched.");
                    }

                    return args[position + (hasReceiver ? 1 : 0)];
                })
                .ToImmutableList();

        private static bool SameKeys(ImmutableList<V> owed, ImmutableList<V> given) =>
            owed.Count == given.Count
            && owed.Zip(given).All(p => p.First is Param && p.First.Equals(p.Second));

        // ── control flow ─────────────────────────────────────────────────────────────────────────────────

        private IEnumerable<State> Branch(State s, IlCallScan.IlInstruction ins)
        {
            var op = ins.OpCode;
            var fallThrough = s with { Pc = ins.Next };

            if (op == OpCodes.Switch)
            {
                var (rest, selector) = Pop(fallThrough);
                if (selector is Const c)
                {
                    return new[] { (uint)c.Value < (uint)ins.Targets!.Length ? rest with { Pc = ins.Targets[c.Value] } : rest };
                }

                return ins.Targets!.Select(t => rest with { Pc = t }).Append(rest);
            }

            bool? taken;
            State after;
            if (op == OpCodes.Brfalse || op == OpCodes.Brfalse_S || op == OpCodes.Brtrue || op == OpCodes.Brtrue_S)
            {
                (after, var value) = Pop(fallThrough);
                bool? isTrue = value switch
                {
                    Const c => c.Value != 0,
                    This or Owner or LocalAddress or FieldAddress => true,
                    _ => null,
                };
                taken = isTrue is null ? null : (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S) == isTrue.Value;
            }
            else
            {
                (after, var right) = Pop(fallThrough);
                (after, var left) = Pop(after);
                taken = left is Const l && right is Const r ? Compare(op, l.Value, r.Value) : null;
            }

            return taken switch
            {
                true => new[] { after with { Pc = ins.Operand } },
                false => new[] { after },
                null => new[] { after with { Pc = ins.Operand }, after },
            };
        }

        private static bool Compare(OpCode op, int l, int r)
        {
            var name = op.Name!.Replace(".s", string.Empty, StringComparison.Ordinal);
            return name switch
            {
                "beq" => l == r,
                "bne.un" => l != r,
                "bge" => l >= r,
                "bge.un" => (uint)l >= (uint)r,
                "bgt" => l > r,
                "bgt.un" => (uint)l > (uint)r,
                "ble" => l <= r,
                "ble.un" => (uint)l <= (uint)r,
                "blt" => l < r,
                "blt.un" => (uint)l < (uint)r,
                _ => throw new InvalidOperationException($"Unmodelled conditional branch {op.Name}."),
            };
        }

        private IEnumerable<State> Return(State s, IlCallScan.IlInstruction ins)
        {
            if (_isStateMachine)
            {
                switch (s.Fields.GetValueOrDefault(StateField, Unknown.Value))
                {
                    case Const { Value: >= 0 }:
                        // A suspension: MoveNext runs again from its entry, in this state, when the awaited task completes.
                        return new[]
                        {
                            s with
                            {
                                Pc = 0,
                                Stack = ImmutableList<V>.Empty,
                                Locals = ImmutableSortedDictionary<int, V>.Empty,
                                Conts = ImmutableList<object>.Empty,
                            },
                        };
                    case Const:
                        break; // -1 / -2: completion
                    default:
                        Report(ins.Offset, s, "returns with an async state the analysis lost — cannot tell a suspension from a completion");
                        return Array.Empty<State>();
                }
            }

            Complete(ins.Offset, s);
            return Array.Empty<State>();
        }

        private void Complete(int offset, State s)
        {
            switch (s.Phase)
            {
                case Phase.Issued:
                    Report(offset, s, $"completes with {_rule.Opening}'s task neither awaited nor followed by {_rule.Discharge}");
                    break;
                case Phase.Owed:
                    Report(offset, s, $"completes without {_rule.Discharge} {_rule.Opening} owes");
                    break;
                case Phase.Discharging:
                    Report(offset, s, $"completes without awaiting {_rule.Discharge}");
                    break;
            }
        }

        private void Report(int offset, State s, string what) =>
            _violations.Add(s.OpenedAt >= 0
                ? $"IL_{offset:X4}: {what} ({_rule.Opening} at IL_{s.OpenedAt:X4})"
                : $"IL_{offset:X4}: {what}");

        /// <summary>The handler search for an exception raised at <paramref name="site"/>, from the enclosing clause at <paramref name="from"/>.</summary>
        private IEnumerable<State> Throw(State s, int site, int from)
        {
            // An exception leaving a running finally abandons that finally's continuation.
            var conts = s.Conts;
            while (conts.Count > 0 && HandlerContains(ClauseOf(conts[^1]), site))
            {
                conts = conts.RemoveAt(conts.Count - 1);
            }

            s = s with { Conts = conts };
            var enclosing = Enclosing(site);
            var successors = new List<State>();
            for (var position = from; position < enclosing.Count; position++)
            {
                var clauseIndex = enclosing[position];
                var clause = _clauses[clauseIndex];
                switch (clause.Flags)
                {
                    case ExceptionHandlingClauseOptions.Clause:
                        successors.Add(s with { Pc = clause.HandlerOffset, Stack = ImmutableList<V>.Empty.Add(Unknown.Value) });
                        if (clause.CatchType == typeof(object) || clause.CatchType == typeof(Exception))
                        {
                            return successors;
                        }

                        break;
                    case ExceptionHandlingClauseOptions.Filter:
                        successors.Add(s with { Pc = clause.FilterOffset, Stack = ImmutableList<V>.Empty.Add(Unknown.Value) });
                        successors.Add(s with { Pc = clause.HandlerOffset, Stack = ImmutableList<V>.Empty.Add(Unknown.Value) });
                        break;
                    default: // Finally / Fault: it runs, then the search continues outward
                        successors.Add(s with
                        {
                            Pc = clause.HandlerOffset,
                            Stack = ImmutableList<V>.Empty,
                            Conts = s.Conts.Add(new UnwindCont(clauseIndex, site, position + 1)),
                        });
                        return successors;
                }
            }

            // Nothing stopped it: the exception leaves the body — the method completes (faulted).
            Complete(site, s);
            return successors;
        }

        private State Leave(State s, int site, int target)
        {
            var crossed = Enclosing(site)
                .Where(c => _clauses[c].Flags is ExceptionHandlingClauseOptions.Finally && !TryContains(_clauses[c], target))
                .ToImmutableList();

            return crossed.Count == 0
                ? s with { Pc = target }
                : s with
                {
                    Pc = _clauses[crossed[0]].HandlerOffset,
                    Conts = s.Conts.Add(new LeaveCont(crossed[0], target, crossed.RemoveAt(0))),
                };
        }

        private IEnumerable<State> EndFinally(State s, int site)
        {
            if (s.Conts.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{IlCallScan.Describe(_body)}: IL_{site:X4} endfinally on a path that did not enter its finally — the path analysis cannot continue it.");
            }

            var cont = s.Conts[^1];
            var rest = s with { Conts = s.Conts.RemoveAt(s.Conts.Count - 1) };
            return cont switch
            {
                LeaveCont { Remaining.Count: > 0 } leave => new[]
                {
                    rest with
                    {
                        Pc = _clauses[leave.Remaining[0]].HandlerOffset,
                        Conts = rest.Conts.Add(new LeaveCont(leave.Remaining[0], leave.Target, leave.Remaining.RemoveAt(0))),
                    },
                },
                LeaveCont leave => new[] { rest with { Pc = leave.Target } },
                UnwindCont unwind => Throw(rest, unwind.Site, unwind.NextPosition),
                _ => throw new InvalidOperationException("Unknown continuation."),
            };
        }

        private static int ClauseOf(object cont) => cont switch
        {
            LeaveCont l => l.Clause,
            UnwindCont u => u.Clause,
            _ => -1,
        };

        /// <summary>
        /// The clauses whose TRY range encloses <paramref name="site"/>, innermost first: by try length, and in metadata order
        /// among the clauses of one try (a stable sort).
        /// </summary>
        private List<int> Enclosing(int site) =>
            Enumerable.Range(0, _clauses.Count)
                .Where(c => TryContains(_clauses[c], site))
                .OrderBy(c => _clauses[c].TryLength)
                .ToList();

        private static bool TryContains(ExceptionHandlingClause clause, int offset) =>
            offset >= clause.TryOffset && offset < clause.TryOffset + clause.TryLength;

        private bool HandlerContains(int clauseIndex, int offset) =>
            clauseIndex >= 0
            && offset >= _clauses[clauseIndex].HandlerOffset
            && offset < _clauses[clauseIndex].HandlerOffset + _clauses[clauseIndex].HandlerLength;

        // ── values ───────────────────────────────────────────────────────────────────────────────────────

        private static (State Remaining, V Value) Pop(State s)
        {
            if (s.Stack.Count == 0)
            {
                throw new InvalidOperationException($"Evaluation stack underflow at IL_{s.Pc:X4} — the path analysis decoded the body wrongly.");
            }

            return (s with { Stack = s.Stack.RemoveAt(s.Stack.Count - 1) }, s.Stack[^1]);
        }

        /// <summary>Only the dispatch local and the state field keep constants; elsewhere a constant could grow without bound in a loop.</summary>
        private static V Widen(V value) => value is Const ? Unknown.Value : value;

        private static V Resolve(State s, V value) => value switch
        {
            LocalAddress l => s.Locals.GetValueOrDefault(l.Index, Unknown.Value),
            FieldAddress f => s.Fields.GetValueOrDefault(f.Name, Unknown.Value),
            _ => value,
        };

        private static State Forget(State s, V address) => address switch
        {
            LocalAddress l => s with { Locals = s.Locals.SetItem(l.Index, Unknown.Value) },
            FieldAddress f => s with { Fields = s.Fields.SetItem(f.Name, Unknown.Value) },
            _ => s,
        };

        private V FieldValue(State s, FieldInfo field)
        {
            if (s.Fields.TryGetValue(field.Name, out var known))
            {
                return known;
            }

            if (_isStateMachine && field.DeclaringType == _body.DeclaringType)
            {
                if (field.Name == OwnerField)
                {
                    return Owner.Value;
                }

                if (_sourceParameters.Contains(field.Name))
                {
                    return new Param(field.Name); // a hoisted parameter, never re-assigned on this path
                }
            }

            return Unknown.Value;
        }

        private V ArgValue(State s, int index)
        {
            if (s.Args.TryGetValue(index, out var known))
            {
                return known;
            }

            if (!_body.IsStatic)
            {
                if (index == 0)
                {
                    return This.Value;
                }

                index--;
            }

            return !_isStateMachine && index < _bodyParameters.Length ? new Param(_bodyParameters[index].Name!) : Unknown.Value;
        }

        private FieldInfo ResolveField(int token) =>
            IlCallScan.Resolve(_body, token, (module, t, ta, ma) => module.ResolveField(t, ta, ma))
            ?? throw new InvalidOperationException($"{IlCallScan.Describe(_body)}: unresolvable field token 0x{token:X8}.");

        // ── opcode tables ────────────────────────────────────────────────────────────────────────────────

        private static int? LocalIndex(IlCallScan.IlInstruction ins, bool store)
        {
            var op = ins.OpCode;
            if (store)
            {
                return op == OpCodes.Stloc_0 ? 0 : op == OpCodes.Stloc_1 ? 1 : op == OpCodes.Stloc_2 ? 2 : op == OpCodes.Stloc_3 ? 3
                    : op == OpCodes.Stloc_S || op == OpCodes.Stloc ? ins.Operand : null;
            }

            return op == OpCodes.Ldloc_0 ? 0 : op == OpCodes.Ldloc_1 ? 1 : op == OpCodes.Ldloc_2 ? 2 : op == OpCodes.Ldloc_3 ? 3
                : op == OpCodes.Ldloc_S || op == OpCodes.Ldloc ? ins.Operand : null;
        }

        private static int? ArgIndex(IlCallScan.IlInstruction ins)
        {
            var op = ins.OpCode;
            return op == OpCodes.Ldarg_0 ? 0 : op == OpCodes.Ldarg_1 ? 1 : op == OpCodes.Ldarg_2 ? 2 : op == OpCodes.Ldarg_3 ? 3
                : op == OpCodes.Ldarg_S || op == OpCodes.Ldarg ? ins.Operand : null;
        }

        private static int? IntConstant(IlCallScan.IlInstruction ins)
        {
            var op = ins.OpCode;
            if (op == OpCodes.Ldc_I4_M1)
            {
                return -1;
            }

            if (op.Value >= OpCodes.Ldc_I4_0.Value && op.Value <= OpCodes.Ldc_I4_8.Value && op.Size == 1)
            {
                return op.Value - OpCodes.Ldc_I4_0.Value;
            }

            return op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4 ? ins.Operand : null;
        }

        private static int Pops(OpCode op) => op.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1
                or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref
                or StackBehaviour.Popref_popi_pop1 => 3,
            _ => throw new InvalidOperationException($"Unmodelled stack behaviour for {op.Name}."),
        };

        private static int Pushes(OpCode op) => op.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4
                or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            _ => throw new InvalidOperationException($"Unmodelled stack behaviour for {op.Name}."),
        };

        /// <summary>The straight-line instructions that can raise an exception (calls and field access are decided elsewhere).</summary>
        private static bool MayThrow(OpCode op)
        {
            var name = op.Name!;
            return name.StartsWith("ldelem", StringComparison.Ordinal)
                   || name.StartsWith("stelem", StringComparison.Ordinal)
                   || name.StartsWith("ldind.", StringComparison.Ordinal)
                   || name.StartsWith("stind.", StringComparison.Ordinal)
                   || name.Contains(".ovf", StringComparison.Ordinal)
                   || name is "castclass" or "unbox" or "unbox.any" or "ldlen" or "newarr" or "div" or "div.un" or "rem"
                       or "rem.un" or "ckfinite" or "ldobj" or "stobj" or "cpobj" or "localloc" or "initblk" or "cpblk"
                       or "refanyval" or "ldvirtftn" or "ldsfld" or "ldsflda" or "stsfld" or "mkrefany";
        }
    }

    /// <summary>
    /// The await plumbing every <c>await</c> compiles to — <c>ConfigureAwait</c> / <c>GetAwaiter</c> on a task,
    /// <c>GetAwaiter</c> / <c>IsCompleted</c> on an awaitable or awaiter, the async method builder's scheduling and
    /// completion members — plus <c>CancellationToken.None</c> and <c>string.Concat</c>. None of them fails except by
    /// running out of memory, which the model does not include; none writes through an address it is given in a way the
    /// analysis tracks. (<c>GetResult</c> is NOT here: it rethrows the awaited operation's exception, and the analysis
    /// models both of its edges.)
    /// </summary>
    internal static bool IsAwaitPlumbing(MethodBase method)
    {
        var declaring = method.DeclaringType;
        if (declaring is null)
        {
            return false;
        }

        var definition = declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring;
        if ((definition == typeof(Task) || definition == typeof(Task<>) || definition == typeof(ValueTask) || definition == typeof(ValueTask<>))
            && method.Name is "ConfigureAwait" or "GetAwaiter")
        {
            return true;
        }

        if (declaring.Namespace == "System.Runtime.CompilerServices")
        {
            if ((declaring.Name.Contains("Awaiter", StringComparison.Ordinal) || declaring.Name.Contains("Awaitable", StringComparison.Ordinal))
                && method.Name is "GetAwaiter" or "get_IsCompleted")
            {
                return true;
            }

            if (declaring.Name.StartsWith("Async", StringComparison.Ordinal) && declaring.Name.Contains("MethodBuilder", StringComparison.Ordinal)
                && method.Name is "AwaitUnsafeOnCompleted" or "AwaitOnCompleted" or "SetResult" or "SetException" or "Start"
                    or "Create" or "get_Task" or "SetStateMachine")
            {
                return true;
            }
        }

        return (declaring == typeof(CancellationToken) && method.Name == "get_None")
               || (declaring == typeof(string) && method.Name == "Concat");
    }

    private static bool IsAwaitable(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return definition == typeof(Task) || definition == typeof(Task<>) || definition == typeof(ValueTask) || definition == typeof(ValueTask<>);
    }

    /// <summary>
    /// Whether a call can write through the address it receives at <paramref name="position"/>: a <c>ref</c> / <c>out</c>
    /// parameter (not <c>in</c>), or the receiver of a struct method that is neither on a readonly struct nor itself readonly.
    /// </summary>
    private static bool WritesThrough(MethodBase target, ParameterInfo[] parameters, bool hasReceiver, int position)
    {
        if (hasReceiver && position == 0)
        {
            var declaring = target.DeclaringType;
            return !(declaring is { IsValueType: true } && (IsReadOnly(declaring) || IsReadOnly(target)));
        }

        var parameter = parameters[position - (hasReceiver ? 1 : 0)];
        return parameter.ParameterType.IsByRef && !parameter.IsIn && !IsReadOnly(parameter);
    }

    private const string IsReadOnlyAttribute = "System.Runtime.CompilerServices.IsReadOnlyAttribute";

    private static bool IsReadOnly(MemberInfo member) =>
        member.CustomAttributes.Any(a => a.AttributeType.FullName == IsReadOnlyAttribute);

    private static bool IsReadOnly(ParameterInfo parameter) =>
        parameter.CustomAttributes.Any(a => a.AttributeType.FullName == IsReadOnlyAttribute);
}
