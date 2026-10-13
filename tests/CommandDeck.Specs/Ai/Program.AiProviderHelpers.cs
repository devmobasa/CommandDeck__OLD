using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class TrackingCredentialResolver( string value ) : IAiCredentialResolver
    {
        public AiCredentialLease? LastLease { get; private set; }

        public ValueTask<AiCredentialLease?> ResolveAsync(
            AiCredentialOptions reference,
            CancellationToken cancellationToken )
        {
            LastLease = new AiCredentialLease( Encoding.UTF8.GetBytes( value ) );
            return ValueTask.FromResult<AiCredentialLease?>( LastLease );
        }
    }
}
