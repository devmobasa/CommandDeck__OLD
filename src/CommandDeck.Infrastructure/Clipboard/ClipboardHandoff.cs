namespace CommandDeck;

public enum ClipboardHandoffPhase
{
    Charged, DispatchArmed, Observed, Retired
}
public enum ClipboardEffectOutcome
{
    NotSent, NoExec, ExecConfirmed, Unknown
}
public enum ClipboardManagerWork
{
    CallInFlight, JobPending, NoneProven, Unverifiable
}

/// <summary>Private bookkeeping identity; never contains a clipboard destination.</summary>
public sealed record ClipboardHandoff(
    int Slot,
    Guid Attempt,
    Guid Owner,
    string BootId,
    string WorkerIdentity,
    string ManagerIdentity,
    ClipboardHandoffPhase Phase,
    ClipboardEffectOutcome Outcome,
    ClipboardManagerWork Work,
    bool WorkerReaped )
{
    public string UnitName => $"commanddeck-clipboard-{Attempt:N}.service";
    public bool Charged => Phase != ClipboardHandoffPhase.Retired;
}

/// <summary>Evidence supplied only by the supervisor/manager adapter, never IPC clients.</summary>
public enum ClipboardHandoffEvidence
{
    MissingOrInconsistent,
    StartJobPending,
    ExecConfirmed,
    TerminalEmptyUnit,
    ProvenPreExecFailure
}
