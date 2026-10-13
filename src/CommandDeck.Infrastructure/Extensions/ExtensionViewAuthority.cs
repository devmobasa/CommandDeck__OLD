using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck;

public sealed record PresentedExtensionView( string SessionId, long Epoch, string ViewId, long Revision, JsonElement Content );

/// <summary>Owns displayed content and action admission; no child RPC or host effect runs under its lock.</summary>
public sealed class ExtensionViewAuthority( string sessionId, long epoch, long grantRevision, Func<string, string>? resolveAsset = null )
{
    private readonly object _gate = new( );
    private readonly List<Snapshot> _offered = [];
    private readonly Dictionary<string, ExtensionActionAdmission> _admitted = new( StringComparer.Ordinal );
    private readonly List<Snapshot> _parents = [];
    private Snapshot? _latest;
    private Snapshot? _presented;
    private long _revision;
    private long _ingress;
    private long _generation;
    private ExtensionInvocation? _invocation;
    private bool _invocationActive;
    private bool _revoked;
    private bool _effectConsumed;

    public ExtensionInvocation BeginInvocation( InvocationOrigin origin )
    {
        lock ( _gate )
        {
            EnsureLive( );
            _generation = checked(_generation + 1);
            _invocation = new( _generation, Guid.NewGuid( ).ToString( "N" ), origin );
            _invocationActive = true;
            _effectConsumed = false;
            return _invocation;
        }
    }

    public void CompleteInvocation( ExtensionInvocation invocation )
    {
        lock ( _gate )
        {
            if ( invocation == _invocation )
            {
                _invocationActive = false;
            }
        }
    }

    public void AdmitCopyEffect( CopyTextRequest request, long currentGrantRevision, IReadOnlySet<string> grants )
    {
        ViewValidation.ValidateCopyText( request.Text );
        lock ( _gate )
        {
            EnsureLive( );
            EnsureInvocation( request.SessionId, request.ExecutionEpoch, request.Generation, request.InvocationId, currentGrantRevision );
            if ( _effectConsumed )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.StaleSession );
            }
            RequireGrant( grants, ExtensionPermissionIds.ClipboardWrite );
            _effectConsumed = true;
        }
    }

    public void AuthorizeData( CommandDataRequest request, long currentGrantRevision, IReadOnlySet<string> grants )
    {
        lock ( _gate )
        {
            EnsureLive( );
            var capability = ExtensionPermissionPolicy.Data( request.Operation );
            EnsureInvocation( request.SessionId, request.ExecutionEpoch, request.Generation, request.InvocationId, currentGrantRevision );
            RequireGrant( grants, capability );
        }
    }

    public PresentedExtensionView Replace( ViewUpdate update ) => Publish( update, false );

    public void AuthorizeBroker( BrokerRequest request, long currentGrantRevision, IReadOnlySet<string> grants )
    {
        lock ( _gate )
        {
            EnsureLive( );
            var capability = ExtensionPermissionPolicy.Broker( request.Operation );
            EnsureInvocation( request.SessionId, request.ExecutionEpoch, request.Generation, request.InvocationId, currentGrantRevision );
            RequireGrant( grants, capability );
        }
    }
    public PresentedExtensionView Push( ViewUpdate update ) => Publish( update, true );

    public void RequireUserActivatedInvocation( )
    {
        lock ( _gate )
        {
            EnsureLive( );
            if ( !_invocationActive || _invocation?.Origin != InvocationOrigin.UserAction )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.PermissionDenied );
            }
        }
    }

    private PresentedExtensionView Publish( ViewUpdate update, bool push )
    {
        // Freeze mutable arrays before validating or binding actions. Future
        // mutations by the caller cannot change an offered action's payload.
        var content = JsonSerializer.Deserialize( JsonSerializer.Serialize( update.Content, ExtensionViewJsonContext.Default.ExtensionView ),
            ExtensionViewJsonContext.Default.ExtensionView );
        if ( content is null )
        {
            throw new InvalidDataException( "The extension view is missing." );
        }

        ViewValidation.Validate( content );
        lock ( _gate )
        {
            EnsureLive( );
            if ( update.SessionId != sessionId || update.ExecutionEpoch != epoch || update.Generation != _generation || !_invocationActive )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.StaleSession );
            }
            ValidateTarget( update, content, push );
            if ( _offered.Count == 2 )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.StaleView );
            }

            // Prepare the complete renderer payload before retiring any displayed authority.
            var snapshot = CreateSnapshot( content, checked(_revision + 1), _parents.Count + (push ? 2 : 1) );
            if ( push )
            {
                _parents.Add( _latest! );
                RetireContext( );
            }
            _revision = snapshot.Rendered.Revision;
            _latest = snapshot;
            _offered.Add( snapshot );
            return snapshot.Rendered;
        }
    }

    private void ValidateTarget( ViewUpdate update, ExtensionView content, bool push )
    {
        if ( push && (_latest is null || _parents.Count >= 7 || _latest.View.Id == content.Id || _parents.Any( value => value.View.Id == content.Id )) )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidView );
        }
        if ( !push && _latest is not null && _latest.View.Id != content.Id )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.StaleView );
        }
        var expected = !push && _latest?.View.Id == content.Id ? _latest.Rendered.Revision : 0;
        if ( update.ViewId != content.Id || update.ExpectedRevision != expected )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.StaleView );
        }
    }

    public PresentedExtensionView Pop( )
    {
        lock ( _gate )
        {
            EnsureLive( );
            if ( _parents.Count == 0 )
            {
                throw new InvalidOperationException( "The view has no parent." );
            }
            var parent = _parents[^1];
            var restored = CreateSnapshot( parent.View, checked(_revision + 1), _parents.Count, parent.Model );
            _parents.RemoveAt( _parents.Count - 1 );
            RetireContext( );
            _latest = restored;
            _revision = restored.Rendered.Revision;
            _offered.Add( _latest );
            return _latest.Rendered;
        }
    }

    private void RetireContext( )
    {
        _admitted.Clear( );
        _offered.Clear( );
        _presented = null;
    }

    public void Present( long ingressSequence, long revision )
    {
        lock ( _gate )
        {
            AdmitSequence( ingressSequence );
            var snapshot = _offered.FirstOrDefault( value => value.Rendered.Revision == revision );
            if ( snapshot is null )
            {
                if ( _presented?.Rendered.Revision == revision )
                {
                    return;
                }
                throw new InvalidOperationException( "The extension view was not offered or has been retired." );
            }
            _presented = snapshot;
            _offered.RemoveAll( value => value.Rendered.Revision <= revision );
        }
    }

    public ExtensionActionAdmission AdmitAction( long ingressSequence, long displayedRevision, string handle, string? itemId,
        long formRevision = 0, Dictionary<string, JsonElement>? values = null )
    {
        lock ( _gate )
        {
            AdmitSequence( ingressSequence );
            if ( _presented?.Rendered.Revision != displayedRevision || !_presented.Actions.TryGetValue( handle, out var binding ) || binding.ItemId != itemId )
            {
                throw new InvalidOperationException( "The extension action is not bound to the displayed view." );
            }
            if ( _admitted.Count >= 32 )
            {
                throw new InvalidOperationException( "Too many extension actions are pending." );
            }
            if ( binding.Binding is PageBinding or BackBinding && _latest != _presented )
            {
                throw new InvalidOperationException( "The navigation control has expired." );
            }
            var admission = new ExtensionActionAdmission( Guid.NewGuid( ).ToString( "N" ), binding.Binding, itemId, grantRevision,
                            _presented.View.Id, displayedRevision );
            if ( binding.Binding is SubmitBinding )
            {
                if ( _latest != _presented || _presented.View is not FormView form || formRevision <= _presented.SubmittedFormRevision )
                {
                    throw new InvalidOperationException( "The form submission is stale or has already been accepted." );
                }
                FormValidation.ValidateSubmission( form.Fields, values );
                admission.Submission = new FormSubmitted( form.Submit.Id, formRevision, values!.ToDictionary( pair => pair.Key, pair => pair.Value.Clone( ), StringComparer.Ordinal ) );
                admission.PreviousSubmittedFormRevision = _presented.SubmittedFormRevision;
                _presented.SubmittedFormRevision = formRevision;
            }
            else if ( values is not null || formRevision != 0 )
            {
                throw new InvalidOperationException( "Only a form submit accepts values." );
            }
            _admitted.Add( admission.Id, admission );
            return admission;
        }
    }

    /// <summary>Consume just before dispatch. A previously admitted action survives a harmless redraw, but never revocation or replay.</summary>
    public ExtensionActionBinding BeginAction( ExtensionActionAdmission admission, long currentGrantRevision, IReadOnlySet<string> grants )
    {
        ArgumentNullException.ThrowIfNull( admission );
        lock ( _gate )
        {
            EnsureLive( );
            if ( currentGrantRevision != grantRevision || !_admitted.Remove( admission.Id, out var pending ) || !ReferenceEquals( pending, admission ) )
            {
                throw new InvalidOperationException( "The extension action authority has expired." );
            }
            if ( admission.Binding is PageBinding or BackBinding && _latest?.Rendered.Revision != admission.Revision )
            {
                throw new InvalidOperationException( "The navigation control has expired." );
            }
            RequireGrant( grants, admission.Binding is EffectBinding effect ? ExtensionPermissionPolicy.Effect( effect.Action.Kind ) : null );
            return admission.Binding;
        }
    }

    /// <summary>A failed child submit can be retried only against the same current form. Revoked or replaced views stay retired.</summary>
    public void RejectSubmission( ExtensionActionAdmission admission )
    {
        lock ( _gate )
        {
            if ( !_revoked && admission.Submission is { } submission && _latest == _presented &&
                _presented?.Rendered.Revision == admission.Revision && _presented.View.Id == admission.ViewId &&
                _presented.SubmittedFormRevision == submission.FormRevision )
            {
                _presented.SubmittedFormRevision = admission.PreviousSubmittedFormRevision;
            }
        }
    }

    public void Revoke( )
    {
        lock ( _gate )
        {
            _revoked = true;
            _invocationActive = false;
            _admitted.Clear( );
            _offered.Clear( );
            _latest = null;
            _presented = null;
            _parents.Clear( );
        }
    }

    private void AdmitSequence( long sequence )
    {
        EnsureLive( );
        if ( sequence != _ingress + 1 )
        {
            throw new InvalidOperationException( "The extension input sequence is stale or out of order." );
        }
        _ingress = sequence;
    }

    private void EnsureInvocation( string currentSession, long currentEpoch, long generation, string invocation, long currentGrantRevision )
    {
        if ( currentSession != sessionId || currentEpoch != epoch || generation != _generation || invocation != _invocation?.Id ||
             !_invocationActive || currentGrantRevision != grantRevision )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.StaleSession );
        }
    }

    private static void RequireGrant( IReadOnlySet<string> grants, string? capability )
    {
        if ( capability is not null && !grants.Contains( capability ) )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.PermissionDenied );
        }
    }

    private void EnsureLive( )
    {
        if ( _revoked )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.StaleSession );
        }
    }

    private Snapshot CreateSnapshot( ExtensionView content, long revision, int depth, RenderedExtensionView? retained = null )
    {
        Dictionary<string, ActionBinding> actions = new( StringComparer.Ordinal );
        string Bind( ExtensionActionBinding binding, string? itemId )
        {
            var handle = Guid.NewGuid( ).ToString( "N" );
            actions.Add( handle, new( itemId, binding ) );
            return handle;
        }
        var model = new ExtensionViewRenderer( resolveAsset, Bind ).Build( content, depth, retained );

        var bytes = JsonSerializer.SerializeToUtf8Bytes( model, RenderedExtensionJsonContext.Default.RenderedExtensionView );
        if ( bytes.Length > 240 * 1024 )
        {
            throw new InvalidDataException( "The rendered view exceeds its limit." );
        }
        var rendered = new PresentedExtensionView( sessionId, epoch, content.Id, revision, JsonElement.Parse( bytes ) );
        return new Snapshot( content, rendered, model, actions );
    }

    private sealed record ActionBinding( string? ItemId, ExtensionActionBinding Binding );
    private sealed record Snapshot( ExtensionView View, PresentedExtensionView Rendered, RenderedExtensionView Model, Dictionary<string, ActionBinding> Actions )
    {
        public long SubmittedFormRevision { get; set; }
    }
}

public sealed class ExtensionActionAdmission
{
    internal ExtensionActionAdmission( string id, ExtensionActionBinding binding, string? itemId, long grantRevision, string viewId, long revision )
    {
        Id = id;
        Binding = binding;
        ItemId = itemId;
        GrantRevision = grantRevision;
        ViewId = viewId;
        Revision = revision;
    }
    internal string Id { get; }
    public ExtensionActionBinding Binding { get; }
    public string? ItemId { get; }
    public long GrantRevision { get; }
    public string ViewId { get; }
    public long Revision { get; }
    public FormSubmitted? Submission { get; internal set; }
    internal long PreviousSubmittedFormRevision { get; set; }
}
