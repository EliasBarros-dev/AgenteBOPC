using DiscordBot.Domain;

namespace DiscordBot.Utils;

public static class Paginar
{
    public static List<List<Artigo>> lista(List<Artigo> lista, int tamanhoPagina)
    {
        return lista
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / tamanhoPagina)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();
    }
}