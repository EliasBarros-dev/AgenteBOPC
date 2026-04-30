using DiscordBot.Domain;

namespace DiscordBot;

public static class BotState
{
    public static Dictionary<ulong, (string descricao, string ilicitos, List<string> artigos)> Cache = new();
    public static List<Artigo> Artigos = new();
    public static List<List<Artigo>> Paginas = new();
    public static Dictionary<ulong, int> PaginaUsuario = new();
    public static Dictionary<ulong, ulong> CanalPorUsuario = new();
    public static Dictionary<ulong, ulong> CanalOrigemPorUsuario = new();
}

