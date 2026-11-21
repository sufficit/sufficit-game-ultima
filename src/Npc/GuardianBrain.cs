namespace Ultima.Npc;

/// <summary>
/// Cérebro da Guardiã do Jardim: máquina de estados do diálogo que decide sobre
/// snapshots e eventos, e devolve comandos. As falas fixas de abertura moram aqui;
/// a WebLLM da GPU do jogador e a API do jogo são produtores externos de respostas,
/// que chegam como <see cref="ReplyArrived"/> e viram <see cref="SetReply"/>.
/// </summary>
public sealed class GuardianBrain
{
    private const string Speaker = "Guardiã do Jardim";
    private const string Player = "Você";
    private const string Thinking = "…";

    private static readonly DialogLine[] DefaultLines =
    [
        new(Speaker, "Olá, caminhante. Você chegou ao jardim que existe para testar uma ideia."),
        new(Player, "Que ideia?"),
        new(Speaker, "Um jogo inteiro escrito em C#, rodando no seu navegador, com o mundo 3D desenhado pela sua placa de vídeo."),
        new(Speaker, "Escreva algo para mim. Se você ativar a IA na sua GPU, eu penso aqui mesmo, no seu navegador."),
        new(Player, "Até lá, boa trilha!")
    ];

    private readonly List<DialogLine> _lines = [.. DefaultLines];
    private int _line = -1;          // -1 = diálogo fechado
    private bool _thinking;
    private string _replySource = string.Empty;

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
        if (!s.PlayerNearGuardian) { return NpcDecision.Empty; }
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
        _lines.Add(new DialogLine(Speaker, Thinking));
        _line = _lines.Count - 1;
        _thinking = true;
        _replySource = string.Empty;
        return View([new BeginThinking(_line)]);
    }

    private NpcDecision OnReply(ReplyArrived reply)
    {
        if (_line < 0 || !_thinking) { return NpcDecision.Empty; }
        var text = reply.Reply.Length > 500 ? reply.Reply[..500] : reply.Reply;
        _lines[_line] = new DialogLine(Speaker, text);
        _thinking = false;
        _replySource = reply.Source;
        return View([new SetReply(_line, text, reply.Source)]);
    }

    private NpcDecision OnIntroChanged(IntroLinesChanged changed)
    {
        if (changed.Lines.Length == 0) { return NpcDecision.Empty; }
        var speaker = string.IsNullOrWhiteSpace(changed.Speaker) ? Speaker : changed.Speaker;
        _lines.Clear();
        _lines.AddRange(changed.Lines.Select(l => new DialogLine(speaker, l)));
        if (_line >= _lines.Count) { _line = _lines.Count - 1; }           // diálogo aberto durante troca de conteúdo
        _thinking = false;
        return _line >= 0 ? View([new ShowLine(_line)]) : NpcDecision.Empty;
    }

    private NpcDecision View(IReadOnlyList<NpcCommand> commands) =>
        new(commands, new DialogView(Speaker, [.. _lines], _line, _thinking, _replySource));
}
