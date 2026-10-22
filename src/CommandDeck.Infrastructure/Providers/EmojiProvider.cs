namespace CommandDeck;

/// <summary>
/// Emoji picker (U4). Managed and offline: it answers only through the
/// <c>emoji:</c>/<c>e:</c> filter, so an ordinary keystroke does no work. It
/// matches a curated set of common emoji by name and keywords; Enter copies the
/// glyph. No subprocess, no network, no persistence.
/// </summary>
public sealed class EmojiProvider : ICommandProvider
{
    private const int MaximumResults = 60;

    // Curated common emoji: (glyph, name, space-separated keywords). Kept a
    // focused subset so the binary stays small; extend as needed.
    private static readonly Emoji[] Catalog =
    [
        new("\U0001F600", "grinning face", "happy smile grin"),
        new("\U0001F603", "grinning face with big eyes", "happy smile joy"),
        new("\U0001F604", "grinning face with smiling eyes", "happy smile joy"),
        new("\U0001F601", "beaming face", "happy smile grin"),
        new("\U0001F606", "grinning squinting face", "laugh happy haha"),
        new("\U0001F605", "grinning face with sweat", "relief phew nervous"),
        new("\U0001F923", "rolling on the floor laughing", "rofl lol haha funny"),
        new("\U0001F602", "face with tears of joy", "laugh cry lol funny"),
        new("\U0001F642", "slightly smiling face", "smile happy"),
        new("\U0001F643", "upside down face", "silly sarcasm irony"),
        new("\U0001F609", "winking face", "wink flirt joke"),
        new("\U0001F60A", "smiling face with smiling eyes", "happy blush warm"),
        new("\U0001F607", "smiling face with halo", "angel innocent"),
        new("\U0001F970", "smiling face with hearts", "love adore crush"),
        new("\U0001F60D", "heart eyes", "love crush adore like"),
        new("\U0001F929", "star struck", "wow amazed excited"),
        new("\U0001F618", "face blowing a kiss", "kiss love flirt"),
        new("\U0001F617", "kissing face", "kiss love"),
        new("\U0001F61A", "kissing face with closed eyes", "kiss love"),
        new("\U0001F60B", "face savoring food", "yum tasty delicious"),
        new("\U0001F61B", "face with tongue", "silly playful"),
        new("\U0001F61C", "winking face with tongue", "silly joke crazy"),
        new("\U0001F92A", "zany face", "crazy silly wild"),
        new("\U0001F914", "thinking face", "think hmm consider ponder"),
        new("\U0001F910", "zipper mouth face", "quiet secret silence"),
        new("\U0001F928", "face with raised eyebrow", "skeptical doubt suspicious"),
        new("\U0001F610", "neutral face", "meh blank"),
        new("\U0001F611", "expressionless face", "blank meh"),
        new("\U0001F636", "face without mouth", "silent speechless blank"),
        new("\U0001F60F", "smirking face", "smug sly flirt"),
        new("\U0001F612", "unamused face", "unimpressed annoyed meh"),
        new("\U0001F644", "face with rolling eyes", "annoyed sarcasm whatever"),
        new("\U0001F62C", "grimacing face", "awkward eek nervous"),
        new("\U0001F925", "lying face", "liar pinocchio nose"),
        new("\U0001F60C", "relieved face", "calm content peaceful"),
        new("\U0001F614", "pensive face", "sad tired reflective"),
        new("\U0001F62A", "sleepy face", "tired sleep"),
        new("\U0001F634", "sleeping face", "sleep zzz tired"),
        new("\U0001F637", "face with medical mask", "sick mask covid"),
        new("\U0001F912", "face with thermometer", "sick ill fever"),
        new("\U0001F915", "face with head bandage", "hurt injured"),
        new("\U0001F922", "nauseated face", "sick gross disgust"),
        new("\U0001F92E", "face vomiting", "sick puke gross"),
        new("\U0001F927", "sneezing face", "sick tissue achoo"),
        new("\U0001F975", "hot face", "heat sweat summer"),
        new("\U0001F976", "cold face", "freezing snow winter"),
        new("\U0001F974", "woozy face", "drunk dizzy tipsy"),
        new("\U0001F635", "dizzy face", "ko dead knocked out"),
        new("\U0001F92F", "exploding head", "mind blown shock wow"),
        new("\U0001F920", "cowboy hat face", "yeehaw western"),
        new("\U0001F973", "partying face", "party celebrate birthday"),
        new("\U0001F60E", "smiling face with sunglasses", "cool sunglasses swag"),
        new("\U0001F913", "nerd face", "geek smart glasses"),
        new("\U0001F9D0", "face with monocle", "inspect examine posh"),
        new("\U0001F615", "confused face", "unsure hmm"),
        new("\U0001F61F", "worried face", "concern anxious"),
        new("\U0001F641", "slightly frowning face", "sad frown"),
        new("\U0001F62E", "face with open mouth", "surprise wow shock"),
        new("\U0001F62F", "hushed face", "surprise shock quiet"),
        new("\U0001F632", "astonished face", "shock wow amazed"),
        new("\U0001F633", "flushed face", "blush embarrassed shy"),
        new("\U0001F97A", "pleading face", "puppy eyes beg please"),
        new("\U0001F626", "frowning face with open mouth", "sad shock"),
        new("\U0001F62D", "loudly crying face", "cry sob sad tears"),
        new("\U0001F631", "screaming in fear", "scared shock home alone"),
        new("\U0001F616", "confounded face", "frustrated"),
        new("\U0001F623", "persevering face", "struggle frustrated"),
        new("\U0001F613", "downcast face with sweat", "tired stress"),
        new("\U0001F629", "weary face", "tired exhausted"),
        new("\U0001F62B", "tired face", "exhausted done"),
        new("\U0001F971", "yawning face", "tired bored sleepy"),
        new("\U0001F624", "face with steam from nose", "angry frustrated triumph"),
        new("\U0001F621", "enraged face", "angry mad rage"),
        new("\U0001F620", "angry face", "mad annoyed"),
        new("\U0001F92C", "face with symbols on mouth", "swear curse angry"),
        new("\U0001F608", "smiling face with horns", "devil evil imp"),
        new("\U0001F47F", "angry face with horns", "devil imp evil"),
        new("\U0001F480", "skull", "dead death deadge"),
        new("\U0001F4A9", "pile of poo", "poop crap funny"),
        new("\U0001F921", "clown face", "clown creepy circus"),
        new("\U0001F47B", "ghost", "spooky boo halloween"),
        new("\U0001F47D", "alien", "ufo space extraterrestrial"),
        new("\U0001F916", "robot", "bot ai machine"),
        new("\U0001F383", "jack o lantern", "pumpkin halloween"),
        new("\U0001F638", "grinning cat", "cat happy"),
        new("\U0001F63B", "smiling cat with heart eyes", "cat love"),
        new("❤️", "red heart", "love like heart"),
        new("\U0001F9E1", "orange heart", "love heart"),
        new("\U0001F49B", "yellow heart", "love heart friendship"),
        new("\U0001F49A", "green heart", "love heart"),
        new("\U0001F499", "blue heart", "love heart"),
        new("\U0001F49C", "purple heart", "love heart"),
        new("\U0001F5A4", "black heart", "love heart"),
        new("\U0001F90D", "white heart", "love heart"),
        new("\U0001F494", "broken heart", "heartbreak sad breakup"),
        new("\U0001F495", "two hearts", "love"),
        new("\U0001F49E", "revolving hearts", "love"),
        new("\U0001F496", "sparkling heart", "love"),
        new("\U0001F49D", "heart with ribbon", "love gift"),
        new("\U0001F44D", "thumbs up", "yes like approve good ok"),
        new("\U0001F44E", "thumbs down", "no dislike bad"),
        new("\U0001F44C", "ok hand", "perfect good ok"),
        new("✌️", "victory hand", "peace two"),
        new("\U0001F91E", "crossed fingers", "luck hope wish"),
        new("\U0001F918", "sign of the horns", "rock metal"),
        new("\U0001F919", "call me hand", "shaka hang loose"),
        new("\U0001F448", "backhand index pointing left", "left point"),
        new("\U0001F449", "backhand index pointing right", "right point"),
        new("\U0001F446", "backhand index pointing up", "up point this"),
        new("\U0001F447", "backhand index pointing down", "down point"),
        new("✋", "raised hand", "stop high five hand"),
        new("\U0001F44B", "waving hand", "hello bye wave hi"),
        new("\U0001F91D", "handshake", "deal agreement"),
        new("\U0001F64F", "folded hands", "please thanks pray hope"),
        new("\U0001F4AA", "flexed biceps", "strong muscle gym power"),
        new("\U0001F64C", "raising hands", "celebrate hooray praise"),
        new("\U0001F44F", "clapping hands", "clap applause bravo"),
        new("\U0001F525", "fire", "lit hot flame burn"),
        new("⭐", "star", "favorite gold"),
        new("✨", "sparkles", "shiny clean magic new"),
        new("\U0001F389", "party popper", "celebrate congrats tada"),
        new("\U0001F38A", "confetti ball", "celebrate party"),
        new("\U0001F381", "wrapped gift", "present birthday"),
        new("\U0001F3C6", "trophy", "win award champion"),
        new("\U0001F947", "first place medal", "gold win first"),
        new("\U0001F3AF", "direct hit", "target bullseye goal"),
        new("\U0001F4AF", "hundred points", "100 perfect score"),
        new("✅", "check mark button", "yes done ok correct"),
        new("❌", "cross mark", "no wrong error x"),
        new("⚠️", "warning", "caution alert"),
        new("❓", "question mark", "help unsure"),
        new("❗", "exclamation mark", "important alert"),
        new("\U0001F4A1", "light bulb", "idea bright"),
        new("\U0001F511", "key", "unlock password"),
        new("\U0001F512", "locked", "secure private lock"),
        new("\U0001F4CC", "pushpin", "pin note"),
        new("\U0001F4C5", "calendar", "date schedule"),
        new("☀️", "sun", "sunny clear weather"),
        new("\U0001F319", "crescent moon", "night sleep"),
        new("\U0001F308", "rainbow", "pride color"),
        new("☁️", "cloud", "cloudy weather"),
        new("❄️", "snowflake", "snow cold winter"),
        new("⚡", "high voltage", "lightning electric power fast"),
        new("\U0001F4A7", "droplet", "water drop"),
        new("\U0001F355", "pizza", "food slice"),
        new("\U0001F354", "hamburger", "food burger"),
        new("☕", "hot beverage", "coffee tea"),
        new("\U0001F37A", "beer mug", "drink alcohol"),
        new("\U0001F4BB", "laptop", "computer code work"),
        new("\U0001F4F1", "mobile phone", "smartphone"),
        new("\U0001F3AE", "video game", "gaming controller play"),
        new("\U0001F680", "rocket", "launch ship fast space"),
        new("\U0001F41B", "bug", "insect error"),
        new("⚙️", "gear", "settings config cog"),
        new("\U0001F4E7", "e-mail", "email mail message"),
        new("\U0001F44A", "oncoming fist", "punch bump"),
        new("\U0001F440", "eyes", "look see watch"),
        new("\U0001F9E0", "brain", "smart mind think"),
    ];

    public string Id => ProviderIds.Emoji;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Emoji ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        var text = (query.Text ?? string.Empty).Trim( );
        var limit = Math.Clamp( request.Limit, 1, MaximumResults );
        List<(Emoji Emoji, int Score)> matches = [];
        foreach ( var emoji in Catalog )
        {
            var score = text.Length == 0 ? 1 : ScoreMatch( emoji, text );
            if ( score > 0 )
            {
                matches.Add( (emoji, score) );
            }
        }

        matches.Sort( static ( left, right ) => right.Score.CompareTo( left.Score ) );
        List<SearchResult> results = [];
        foreach ( (var emoji, var _) in matches )
        {
            if ( results.Count >= limit )
            {
                break;
            }
            results.Add( BuildResult( emoji ) );
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( results );
    }

    private static int ScoreMatch( Emoji emoji, string query )
    {
        if ( string.Equals( emoji.Name, query, StringComparison.OrdinalIgnoreCase ) )
        {
            return 100;
        }
        if ( emoji.Name.StartsWith( query, StringComparison.OrdinalIgnoreCase ) )
        {
            return 60;
        }
        if ( emoji.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) )
        {
            return 40;
        }
        return emoji.Keywords.Contains( query, StringComparison.OrdinalIgnoreCase ) ? 20 : 0;
    }

    private static SearchResult BuildResult( Emoji emoji ) => new( )
    {
        Id = "emoji:" + emoji.Name,
        Kind = ResultKinds.Emoji,
        Title = emoji.Glyph + "  " + emoji.Name,
        Subtitle = emoji.Keywords,
        Icon = emoji.Glyph,
        ProviderId = ProviderIds.Emoji,
        SourceId = SourceIds.Emoji,
        Score = 1,
        Details = [new DetailField( "Keywords", emoji.Keywords )],
        Actions =
        [
            new CommandAction
            {
                Id = "copy",
                Title = "Copy Emoji",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = emoji.Glyph }
            }
        ]
    };

    private readonly record struct Emoji( string Glyph, string Name, string Keywords );
}
