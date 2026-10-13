using System.Globalization;
using System.Text;

namespace CommandDeck;

public static class AiContractValidator
{
    public static void ValidateDraft( AiDraft draft )
    {
        ArgumentNullException.ThrowIfNull( draft );
        ValidateIdentifier( draft.ProviderId, 64, "providerId" );
        ValidateUtf8(
            draft.ModelId, AiLimits.MaximumModelIdBytes, "modelId", allowEmpty: false,
            allowLineBreaks: false );
        ValidateUtf8( draft.UserText, AiLimits.MaximumUserPromptBytes, "userText", allowEmpty: false );
        if ( draft.Attachments is null || draft.Attachments.Count > AiLimits.MaximumAttachments ||
            draft.Attachments.Any( static attachment => attachment is null ) )
        {
            throw new InvalidOperationException( "attachments must contain at most four objects." );
        }

        var aggregate = Encoding.UTF8.GetByteCount( draft.UserText );
        foreach ( var attachment in draft.Attachments )
        {
            if ( attachment.Kind is not AiAttachmentKinds.Text and not AiAttachmentKinds.Result and
                not AiAttachmentKinds.File )
            {
                throw new InvalidOperationException( "attachment kind is unsupported." );
            }
            if ( attachment.Kind == AiAttachmentKinds.Text )
            {
                ValidateUtf8( attachment.Text, AiLimits.MaximumAttachmentBytes, "attachment text", false );
                aggregate += Encoding.UTF8.GetByteCount( attachment.Text! );
            }
            else if ( string.IsNullOrWhiteSpace( attachment.ResultId ) )
            {
                throw new InvalidOperationException( "result and file attachments require resultId." );
            }
        }
        if ( aggregate > AiLimits.MaximumTransmittedTextBytes )
        {
            throw new InvalidOperationException( "AI transmitted text exceeds 128 KiB." );
        }
    }

    public static void ValidateIdentifier( string? value, int maximumLength, string label )
    {
        if ( string.IsNullOrEmpty( value ) || value.Length > maximumLength ||
            value.Any( static character => character > 0x7f ||
                !(char.IsAsciiLetterOrDigit( character ) || character is '.' or '_' or '-') ) )
        {
            throw new InvalidOperationException(
                $"{label} must use 1-{maximumLength} ASCII letters, digits, dots, underscores, or hyphens." );
        }
    }

    public static void ValidateUtf8(
        string? value,
        int maximumBytes,
        string label,
        bool allowEmpty,
        bool allowLineBreaks = true )
    {
        if ( value is null || (!allowEmpty && value.Length == 0) ||
            value.Any( character => char.IsControl( character ) &&
                (!allowLineBreaks || character is not '\n' and not '\r' and not '\t') ) ||
            Encoding.UTF8.GetByteCount( value ) > maximumBytes )
        {
            throw new InvalidOperationException( $"{label} is outside supported UTF-8 bounds." );
        }
    }

    public static void ValidateVisibleText(
        string? value,
        int maximumTextElements,
        string label )
    {
        var trimmed = value?.Trim( ) ?? string.Empty;
        var elementStarts = StringInfo.ParseCombiningCharacters( trimmed );
        var containsVisibleRune = false;
        var containsProhibitedRune = false;
        foreach ( var rune in trimmed.EnumerateRunes( ) )
        {
            var category = Rune.GetUnicodeCategory( rune );
            if ( category is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned )
            {
                containsProhibitedRune = true;
            }
            if ( IsVisibleRune( category ) )
            {
                containsVisibleRune = true;
            }
        }
        if ( trimmed.Length == 0 ||
            elementStarts.Length > maximumTextElements ||
            containsProhibitedRune ||
            !containsVisibleRune )
        {
            throw new InvalidOperationException(
                $"{label} must contain 1-{maximumTextElements} visible characters." );
        }
    }

    private static bool IsVisibleRune( UnicodeCategory category ) => category is
        not UnicodeCategory.SpaceSeparator and
        not UnicodeCategory.LineSeparator and
        not UnicodeCategory.ParagraphSeparator and
        not UnicodeCategory.NonSpacingMark and
        not UnicodeCategory.SpacingCombiningMark and
        not UnicodeCategory.EnclosingMark;
}
