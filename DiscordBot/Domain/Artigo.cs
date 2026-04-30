namespace DiscordBot.Domain;

using System.Text.Json.Serialization;

public class Artigo
{
    [JsonPropertyName("codigo")]
    public string Codigo { get; set; }

    [JsonPropertyName("titulo")]
    public string Titulo { get; set; }

    [JsonPropertyName("descricao")]
    public string Descricao { get; set; }

    [JsonPropertyName("pena_meses")]
    public int PenaMeses { get; set; }

    [JsonPropertyName("multa")]
    public int Multa { get; set; }
}