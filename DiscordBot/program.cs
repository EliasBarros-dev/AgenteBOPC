using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot;

class Program
{
    private DiscordSocketClient _client;
    private InteractionService _interactions; 
    private IServiceProvider _services;

    static Task Main(string[] args) => new Program().MainAsync();

    public async Task MainAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Utils", "artigos.json");
        
        if (!File.Exists(path))
            path = Path.Combine("Utils", "artigos.json");

        var json = File.ReadAllText(path);

        BotState.Artigos = JsonSerializer.Deserialize<List<Artigo>>(json)?
            .Where(a => !string.IsNullOrWhiteSpace(a.Codigo))
            .ToList() ?? new List<Artigo>();

        BotState.Paginas = Paginar.lista(BotState.Artigos, 25);

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
            UseInteractionSnowflakeDate = false
        };

        _client = new DiscordSocketClient(config);
        _interactions = new InteractionService(_client);

        _services = new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_interactions)
            .BuildServiceProvider();

        _client.Log += Log;
        _interactions.Log += Log;

        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteraction;

        _client.ModalSubmitted += async modal =>
        {
            if (modal.Data.CustomId == "bopc_modal")
            {
                await modal.DeferAsync(ephemeral: true);
                
                var descricao = modal.Data.Components.First(x => x.CustomId == "descricao").Value;
                var ilicitos = modal.Data.Components.First(x => x.CustomId == "ilicitos").Value;

                BotState.Cache[modal.User.Id] = (descricao, ilicitos, new List<string>());
                BotState.PaginaUsuario[modal.User.Id] = 0;

                GetBopcPaginationUI(modal.User.Id, out var text, out var components);
                await modal.FollowupAsync(text, components: components, ephemeral: false);
            }

            if (modal.Data.CustomId == "bopc_primary_modal")
            {
                await modal.DeferAsync(ephemeral: true);
                
                if (!BotState.Cache.ContainsKey(modal.User.Id))
                    return;

                var primaria = modal.Data.Components.First(x => x.CustomId == "primaria").Value;
                var userData = BotState.Cache[modal.User.Id];

                var artigosFormatados = string.Join("\n",
                    userData.artigos.Select(codigo =>
                    {
                        var art = BotState.Artigos.FirstOrDefault(a => a.Codigo == codigo);
                        return art != null
                            ? $"- Art. {art.Codigo} - {art.Titulo}"
                            : $"- Art. {codigo}";
                    }));

                var ilicitosFormatados = string.Join("\n",
                    userData.ilicitos
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(i => $"- {i.Trim()}"));

                var resposta = string.Join("\n", new[]
                {
                    "**BOLETIM DE OCORRÊNCIA POLICIAL CIVIL (BOPC)**",
                    "",
                    "**RELATO DOS FATOS:**",
                    userData.descricao,
                    "",
                    "**TIPIFICAÇÃO CRIMINAL:**",
                    artigosFormatados,
                    "",
                    "**ILÍCITOS APREENDIDOS:**",
                    ilicitosFormatados,
                    "",
                    $"**PRIMÁRIA:** {primaria}",
                });

                if (BotState.CanalPorUsuario.TryGetValue(modal.User.Id, out var canalId))
                {
                    var channel = _client.GetChannel(canalId) as ITextChannel;
                    if (channel != null)
                    {
                        var btnBuilder = new ComponentBuilder()
                            .WithButton("🗑️ Excluir Canal", "delete_channel", ButtonStyle.Danger);

                        await channel.SendMessageAsync($"Nova ocorrência registrada por <@{modal.User.Id}>:\n\n{resposta}", components: btnBuilder.Build());

                        // Agendar deleção do canal após 2 minutos
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromMinutes(2));
                            try
                            {
                                await channel.DeleteAsync();
                            }
                            catch { /* ignora se já foi deletado */ }
                        });
                    }
                    // Limpar dicionário de canais por usuário
                    BotState.CanalPorUsuario.Remove(modal.User.Id);
                }

                // Responde ao modal
                await modal.FollowupAsync("✅ Ocorrência finalizada e copiada no canal! Este canal será excluído automaticamente em 2 minutos.", ephemeral: true);

                BotState.CanalOrigemPorUsuario.Remove(modal.User.Id);
                BotState.Cache.Remove(modal.User.Id);
                BotState.PaginaUsuario.Remove(modal.User.Id);
            }
        };

        _client.SelectMenuExecuted += async component =>
        {
            if (component.Data.CustomId != "select_artigos")
                return;

            await component.DeferAsync();

            if (!BotState.Cache.ContainsKey(component.User.Id))
                return;

            var userData = BotState.Cache[component.User.Id];
            var paginaAtual = BotState.PaginaUsuario.ContainsKey(component.User.Id) ? BotState.PaginaUsuario[component.User.Id] : 0;
            
            var paginaArtigos = BotState.Paginas.Count > paginaAtual ? BotState.Paginas[paginaAtual] : new List<Artigo>();
            var pageArticleCodes = paginaArtigos.Select(a => a.Codigo).ToList();
            
            var novos = userData.artigos.Where(a => !pageArticleCodes.Contains(a)).ToList();
            novos.AddRange(component.Data.Values);

            BotState.Cache[component.User.Id] = (userData.descricao, userData.ilicitos, novos);

            GetBopcPaginationUI(component.User.Id, out var text, out var uiComponents);

            await component.ModifyOriginalResponseAsync(msg => 
            { 
                msg.Content = text;
                msg.Components = uiComponents; 
            });
        };

        _client.ButtonExecuted += async component =>
        {
            if (component.Data.CustomId == "delete_channel")
            {
                if (component.Channel is ITextChannel ch)
                {
                    try { await ch.DeleteAsync(); } catch { }
                }
                return;
            }

            if (component.Data.CustomId == "start_bopc_process")
            {
                var modal = new ModalBuilder()
                    .WithTitle("Boletim de Ocorrência")
                    .WithCustomId("bopc_modal")
                    .AddTextInput("Descrição dos fatos", "descricao", TextInputStyle.Paragraph)
                    .AddTextInput("Ilícitos apreendidos", "ilicitos", TextInputStyle.Paragraph);

                await component.RespondWithModalAsync(modal.Build());
                return;
            }

            if (!BotState.PaginaUsuario.ContainsKey(component.User.Id))
                return;

            if (component.Data.CustomId == "cancelar")
            {
                await component.RespondAsync("Operação cancelada.", ephemeral: true);
                
                // Deletar canal
                if (BotState.CanalPorUsuario.TryGetValue(component.User.Id, out var canalId))
                {
                    var channel = _client.GetChannel(canalId) as ITextChannel;
                    if (channel != null)
                    {
                        await channel.DeleteAsync();
                    }
                    BotState.CanalPorUsuario.Remove(component.User.Id);
                }

                BotState.CanalOrigemPorUsuario.Remove(component.User.Id);
                BotState.Cache.Remove(component.User.Id);
                BotState.PaginaUsuario.Remove(component.User.Id);
                return;
            }

            var paginaAtual = BotState.PaginaUsuario[component.User.Id];

            if (component.Data.CustomId == "page_next")
            {
                await component.DeferAsync();
                paginaAtual++;
            }
            else if (component.Data.CustomId == "page_prev")
            {
                await component.DeferAsync();
                paginaAtual--;
            }
            else if (component.Data.CustomId == "finalizar")
            {
                if (!BotState.Cache.ContainsKey(component.User.Id))
                    return;

                var userData = BotState.Cache[component.User.Id];

                if (userData.artigos.Count == 0)
                {
                    await component.RespondAsync("Selecione pelo menos 1 artigo antes de finalizar.", ephemeral: true);
                    return;
                }

                var modalPrimary = new ModalBuilder()
                    .WithTitle("Infração Primária")
                    .WithCustomId("bopc_primary_modal")
                    .AddTextInput("Informe a primária", "primaria", TextInputStyle.Short);

                await component.RespondWithModalAsync(modalPrimary.Build());
                return;
            }

            BotState.PaginaUsuario[component.User.Id] = paginaAtual;

            GetBopcPaginationUI(component.User.Id, out var text, out var uiComponents);

            await component.ModifyOriginalResponseAsync(msg => 
            { 
                msg.Content = text;
                msg.Components = uiComponents; 
            });
        };

        await _interactions.AddModulesAsync(typeof(Program).Assembly, _services);

        string token = Environment.GetEnvironmentVariable("TOKEN") ?? "";

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private async Task ReadyAsync()
    {
        try
        {
            await _interactions.RegisterCommandsGloballyAsync(true);
            Console.WriteLine("Comandos validados globalmente em todos os servidores!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao registrar comandos globais: {ex.Message}");
        }

        // Limpar fila de eventos mortos pendentes no Discord
        _client.Ready -= ReadyAsync; // Desuscreve para evitar chamadas múltiplas
        
        Console.WriteLine("Bot pronto 🚀");
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            var diff = DateTimeOffset.UtcNow.Subtract(interaction.CreatedAt).TotalSeconds;
            Console.WriteLine($"[Interaction] Recebida. Diff de tempo: {diff}s");

            if (diff > 15) 
            {
                Console.WriteLine("⚠️ Ignorando interação muito velha pendente do Discord.");
                return;
            }

            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    private void GetBopcPaginationUI(ulong userId, out string text, out MessageComponent components)
    {
        var paginaAtual = BotState.PaginaUsuario.ContainsKey(userId) ? BotState.PaginaUsuario[userId] : 0;
        var userData = BotState.Cache.ContainsKey(userId) ? BotState.Cache[userId] : (descricao: "", ilicitos: "", artigos: new List<string>());

        if (BotState.Paginas.Count == 0)
        {
            text = "Nenhum artigo encontrado no sistema.";
            components = new ComponentBuilder().Build();
            return;
        }

        if (paginaAtual >= BotState.Paginas.Count) 
            paginaAtual = 0;

        var paginaArtigos = BotState.Paginas[paginaAtual];

        var select = new SelectMenuBuilder()
            .WithCustomId("select_artigos")
            .WithPlaceholder($"Selecione os artigos (Pág. {paginaAtual + 1}/{BotState.Paginas.Count})")
            .WithMinValues(0)
            .WithMaxValues(paginaArtigos.Count > 0 ? paginaArtigos.Count : 1);

        foreach (var artigo in paginaArtigos)
        {
            if (string.IsNullOrWhiteSpace(artigo.Codigo)) continue;

            var isSelected = userData.artigos.Contains(artigo.Codigo);
            var label = $"Art. {artigo.Codigo} - {artigo.Titulo}";
            if (label.Length > 100) label = label.Substring(0, 100);

            select.AddOption(label, artigo.Codigo, isDefault: isSelected);
        }

        var btnAnterior = new ButtonBuilder().WithLabel("Anterior").WithCustomId("page_prev").WithStyle(ButtonStyle.Secondary).WithDisabled(paginaAtual == 0);
        var btnPagina = new ButtonBuilder().WithLabel($"{paginaAtual + 1} / {BotState.Paginas.Count}").WithCustomId("page_indicator").WithStyle(ButtonStyle.Secondary).WithDisabled(true);
        var btnProxima = new ButtonBuilder().WithLabel("Próxima").WithCustomId("page_next").WithStyle(ButtonStyle.Primary).WithDisabled(paginaAtual == BotState.Paginas.Count - 1);

        var btnCancelar = new ButtonBuilder().WithLabel("Cancelar").WithCustomId("cancelar").WithStyle(ButtonStyle.Danger).WithEmote(new Emoji("❌"));
        var btnFinalizar = new ButtonBuilder().WithLabel("Finalizar BOPC").WithCustomId("finalizar").WithStyle(ButtonStyle.Success).WithEmote(new Emoji("✅")).WithDisabled(userData.artigos.Count == 0);

        var builder = new ComponentBuilder()
            .WithSelectMenu(select, row: 0)
            .WithButton(btnAnterior, row: 1)
            .WithButton(btnPagina, row: 1)
            .WithButton(btnProxima, row: 1)
            .WithButton(btnCancelar, row: 2)
            .WithButton(btnFinalizar, row: 2);

        var textoSelecionados = userData.artigos.Count > 0 
            ? $"**Artigos Selecionados ({userData.artigos.Count}):**\n" + string.Join(", ", userData.artigos.Select(a => $"`Art. {a}`"))
            : "**Nenhum artigo selecionado ainda.**";
            
        text = $"👮 **Tipificação Criminal**\n\n{textoSelecionados}\n\n*Use o menu abaixo para marcar ou desmarcar os artigos e navegue pelas páginas.*";
        components = builder.Build();
    }
}