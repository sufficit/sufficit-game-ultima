namespace Ultima.Npc;

/// <summary>
/// Cérebro de diálogo genérico de um NPC conversável: máquina de estados das falas
/// que decide sobre snapshots e eventos, e devolve comandos. As falas de abertura
/// moram no cérebro; a WebLLM da GPU do jogador e a API do jogo são produtores
/// externos de respostas, que chegam como <see cref="ReplyArrived"/> e viram
/// <see cref="SetReply"/>. Ver <see cref="GuardianBrain"/> e
/// <see cref="WandererBrain"/> para os NPCs concretos.
/// </summary>
public abstract class DialogBrain
{
    private const string Player = "Você";
    private const string Thinking = "…";

    private readonly string _speaker;
    private readonly List<DialogLine> _lines;
    private int _line = -1;          // -1 = diálogo fechado
    private bool _thinking;
    private string _replySource = string.Empty;

    protected DialogBrain(string speaker, IReadOnlyList<DialogLine> openingLines)
    {
        _speaker = speaker;
        _lines = [.. openingLines];
    }

    public string Speaker => _speaker;

    public NpcDecision Decide(WorldSnapshot s, GameEvent e) => e switch
    {
        PlayerInteracted => OnInteracted(s),
        DialogClosedByPlayer => OnClosed(),
        PlayerSpoke spoke => OnPlayerSpoke(spoke),
        ReplyArrived reply => OnReply(reply),
        IntroLinesChanged changed => OnIntroChanged(changed),
        _ => NpcDecision.Empty
    };

    private NpcDecision OnInteracted(WorldSnapshot s)
    {
        if (_line < 0)
        {
            _line = 0;
            return View([new OpenDialog(), new ShowLine(0)]);
        }
        if (_thinking) { return View([]); }              // resposta pendente: ignora avanços
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
        return NpcDecision.Empty;                        // a UI já fechou; cérebro apenas sincroniza
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
        _lines.Clear();
        _lines.AddRange(changed.Lines.Select(l => new DialogLine(speaker, l)));
        if (_line >= _lines.Count) { _line = _lines.Count - 1; }           // diálogo aberto durante troca de conteúdo
        _thinking = false;
        return _line >= 0 ? View([new ShowLine(_line)]) : NpcDecision.Empty;
    }

    private NpcDecision View(IReadOnlyList<NpcCommand> commands) =>
        new(commands, new DialogView(_speaker, [.. _lines], _line, _thinking, _replySource));
}
