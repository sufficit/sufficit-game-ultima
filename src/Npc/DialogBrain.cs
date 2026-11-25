namespace Ultima.Npc;

/// <summary>Runs authored dialogue and accepts asynchronous replies as game events.</summary>
public abstract class DialogBrain
{
    private const string Player = "Você";
    private const string Thinking = "…";

    private readonly string _speaker;
    private IReadOnlyList<DialogLine> _opening;
    private readonly List<DialogLine> _lines;
    private readonly IReadOnlyList<DialogTopic> _topics;
    private readonly IReadOnlyList<DialogChoice> _choices;
    private int _line = -1;          // -1 means the dialogue is closed.
    private bool _thinking;
    private string _replySource = string.Empty;

    protected DialogBrain(string speaker, IReadOnlyList<DialogLine> openingLines, IReadOnlyList<DialogTopic> topics)
    {
        _speaker = speaker;
        _opening = openingLines;
        _lines = [.. openingLines];
        _topics = topics;
        _choices = Array.AsReadOnly(topics.Select(topic => new DialogChoice(topic.Id, topic.Prompt)).ToArray());
    }

    public string Speaker => _speaker;

    public NpcDecision Decide(WorldSnapshot s, GameEvent e) => e switch
    {
        PlayerInteracted => OnInteracted(s),
        DialogClosedByPlayer => OnClosed(),
        DialogTopicSelected selected => OnTopicSelected(selected.Id),
        PlayerSpoke spoke => OnPlayerSpoke(spoke),
        ReplyArrived reply => OnReply(reply),
        IntroLinesChanged changed => OnIntroChanged(changed),
        _ => NpcDecision.Empty
    };

    private NpcDecision OnInteracted(WorldSnapshot s)
    {
        if (_line < 0)
        {
            _lines.Clear();
            _lines.AddRange(_opening);
            _replySource = string.Empty;
            _line = 0;
            return View([new OpenDialog(), new ShowLine(0)]);
        }
        if (_thinking) { return View([]); }
        if (_line + 1 < _lines.Count)
        {
            _line += 1;
            return View([new ShowLine(_line)]);
        }
        _line = -1;
        _thinking = false;
        return View([new CloseDialog()]);
    }

    private NpcDecision OnClosed()
    {
        _line = -1;
        _thinking = false;
        return NpcDecision.Empty;
    }

    private NpcDecision OnTopicSelected(string id)
    {
        if (_line < 0 || _thinking) return NpcDecision.Empty;
        var topic = _topics.FirstOrDefault(topic => topic.Id == id);
        if (topic is null) return NpcDecision.Empty;

        // Preserve the transcript up to the line the player has actually seen.
        _lines.RemoveRange(_line + 1, _lines.Count - _line - 1);
        _lines.Add(new DialogLine(Player, topic.Prompt));
        _lines.Add(new DialogLine(_speaker, topic.Reply));
        _line = _lines.Count - 1;
        _replySource = string.Empty;
        return View([new ShowLine(_line)]);
    }

    private NpcDecision OnPlayerSpoke(PlayerSpoke spoke)
    {
        if (_line < 0 || _thinking) { return NpcDecision.Empty; }
        var text = spoke.Text.Length > 300 ? spoke.Text[..300] : spoke.Text;
        _lines.Add(new DialogLine(Player, text));
        _lines.Add(new DialogLine(_speaker, Thinking));
        _line = _lines.Count - 1;
        _thinking = true;
        _replySource = string.Empty;
        return View([new BeginThinking(_line)]);
    }

    private NpcDecision OnReply(ReplyArrived reply)
    {
        if (_line < 0 || !_thinking) { return NpcDecision.Empty; }
        var text = reply.Reply.Length > 500 ? reply.Reply[..500] : reply.Reply;
        _lines[_line] = new DialogLine(_speaker, text);
        _thinking = false;
        _replySource = reply.Source;
        return View([new SetReply(_line, text, reply.Source)]);
    }

    private NpcDecision OnIntroChanged(IntroLinesChanged changed)
    {
        if (changed.Lines.Length == 0) { return NpcDecision.Empty; }
        var speaker = string.IsNullOrWhiteSpace(changed.Speaker) ? _speaker : changed.Speaker;
        _opening = changed.Lines.Select(line => new DialogLine(speaker, line)).ToArray();
        if (_line >= 0) return NpcDecision.Empty;
        _lines.Clear();
        _lines.AddRange(_opening);
        return NpcDecision.Empty;
    }

    private NpcDecision View(IReadOnlyList<NpcCommand> commands) =>
        new(commands, new DialogView(_speaker, [.. _lines], _line, _thinking, _replySource, _line >= 0 && !_thinking ? _choices : []));
}
