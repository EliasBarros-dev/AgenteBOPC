using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;

public class BopcModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("bopc", "Criar boletim de ocorrência")]
    public async Task CriarBopc()
    {
        try
        {
            await DeferAsync(ephemeral: true);

            var guildId = Context.Interaction.GuildId;
            IGuild guild = Context.Guild;

            Console.WriteLine($"DEBUG: Context.Guild = {guild}");
            Console.WriteLine($"DEBUG: Context.Interaction.GuildId = {guildId}");
            Console.WriteLine($"DEBUG: Context.Client type = {Context.Client.GetType()}");

            // Se guild for nula mas temos guildId, tenta obter
            if (guild == null && guildId.HasValue)
            {
                Console.WriteLine($"DEBUG: Tentando obter guild com ID {guildId.Value}");
                if (Context.Client is DiscordSocketClient socketClient)
                {
                    guild = socketClient.GetGuild(guildId.Value);
                }

                if (guild == null)
                {
                    guild = await Context.Client.Rest.GetGuildAsync(guildId.Value);
                }
                
                if (guild != null)
                    Console.WriteLine($"DEBUG: Guild obtida: {guild.Name}");
                else
                    Console.WriteLine($"DEBUG: Guild não encontrada na cache e na API");
            }

            // Validar se o comando foi executado em um servidor
            if (guild == null)
            {
                // Se não encontramos a guild, o bot não está no servidor
                var inviteUrl = $"https://discord.com/api/oauth2/authorize?client_id={Context.Client.CurrentUser.Id}&permissions=8&scope=bot%20applications.commands";
                await FollowupAsync($"❌ Erro: O bot não está neste servidor ou não tem as permissões corretas!\n\nProvavelmente você instalou o bot apenas no seu perfil de usuário. Por favor, adicione o bot ao servidor usando o link abaixo:\n{inviteUrl}", ephemeral: true);
                return;
            }

            var user = Context.User;

            // Criar canal privado para o usuário
            var channelName = $"bopc-{user.Username}-{Guid.NewGuid().ToString().Substring(0, 8)}";
            var permissions = new[]
            {
                new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
            };

            var channel = await guild.CreateTextChannelAsync(channelName, properties => properties.PermissionOverwrites = permissions);
            
            BotState.CanalPorUsuario[user.Id] = channel.Id;
            BotState.CanalOrigemPorUsuario[user.Id] = Context.Channel.Id;

            Console.WriteLine($"✅ Canal criado: {channelName} (ID: {channel.Id}) para usuário {user.Username}");

            var builder = new ComponentBuilder()
                .WithButton("👉 Iniciar BOPC", "start_bopc_process", ButtonStyle.Primary);

            await channel.SendMessageAsync($"Olá <@{user.Id}>, clique no botão abaixo para preencher o seu Boletim de Ocorrência.", components: builder.Build());

            await FollowupAsync($"✅ Canal privado criado para você preencher os dados: <#{channel.Id}>", ephemeral: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erro ao criar BOPC: {ex.Message}\n{ex.StackTrace}");
            if (!Context.Interaction.HasResponded)
                await RespondAsync($"❌ Erro interno: {ex.Message}", ephemeral: true);
            else
                await FollowupAsync($"❌ Erro interno: {ex.Message}", ephemeral: true);
        }
    }
}