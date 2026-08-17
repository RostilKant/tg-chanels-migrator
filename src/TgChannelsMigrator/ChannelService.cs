using TL;
using WTelegram;

namespace TgChannelsMigrator;

/// <summary>
/// Reads the channel list for a logged-in account and performs join/leave operations,
/// automatically waiting out Telegram's FLOOD_WAIT rate limits.
/// </summary>
public static class ChannelService
{
    public static async Task<List<ChannelInfo>> GetChannelsAsync(Client client)
    {
        var dialogs = await client.Messages_GetAllDialogs();
        var result = new List<ChannelInfo>();

        foreach (var (id, chatBase) in dialogs.chats)
        {
            if (chatBase is not Channel channel) continue;
            if ((channel.flags & Channel.Flags.left) != 0) continue; // no longer a member, skip

            var isCreator = (channel.flags & Channel.Flags.creator) != 0;
            var canInvite = isCreator ||
                (channel.admin_rights != null && (channel.admin_rights.flags & ChatAdminRights.Flags.invite_users) != 0);
            var isMegagroup = (channel.flags & Channel.Flags.megagroup) != 0;

            result.Add(new ChannelInfo(
                channel.id,
                channel.access_hash,
                channel.Title,
                channel.MainUsername,
                isCreator,
                canInvite,
                isMegagroup));
        }

        return result.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static InputChannel ToInputChannel(ChannelInfo info) => new(info.Id, info.AccessHash);

    private static InputPeer ToInputPeer(ChannelInfo info) => new InputPeerChannel(info.Id, info.AccessHash);

    /// <summary>
    /// Joins <paramref name="targetClient"/> to a channel that <paramref name="sourceClient"/> is already in.
    /// Public channels are joined directly by username; private ones need the source account
    /// to export an invite link (requires the source to be creator/admin with invite rights).
    /// </summary>
    public static async Task<(bool ok, string message)> JoinChannelAsync(
        Client sourceClient, Client targetClient, ChannelInfo channel)
    {
        try
        {
            if (!string.IsNullOrEmpty(channel.Username))
            {
                var resolved = await RunWithFloodWaitAsync(() => targetClient.Contacts_ResolveUsername(channel.Username!));
                if (resolved.Channel is { } resolvedChannel)
                {
                    await RunWithFloodWaitAsync(() => targetClient.Channels_JoinChannel(resolvedChannel));
                    return (true, "joined via public username");
                }
                return (false, "username resolved to something that isn't a channel");
            }

            if (!channel.CanInvite)
                return (false, "private channel and source account can't export an invite link (not admin/creator) - join it manually");

            var invite = await RunWithFloodWaitAsync(() => sourceClient.Messages_ExportChatInvite(ToInputPeer(channel)));
            if (invite is not ChatInviteExported chatInvite || string.IsNullOrEmpty(chatInvite.link))
                return (false, "could not export an invite link for this channel");

            var hash = ExtractInviteHash(chatInvite.link);
            if (hash == null)
                return (false, $"could not parse invite link: {chatInvite.link}");

            await RunWithFloodWaitAsync(() => targetClient.Messages_ImportChatInvite(hash));
            return (true, "joined via invite link");
        }
        catch (RpcException ex) when (ex.Message == "USER_ALREADY_PARTICIPANT")
        {
            return (true, "target account is already a member");
        }
        catch (RpcException ex)
        {
            return (false, $"Telegram error: {ex.Message}");
        }
    }

    public static async Task<(bool ok, string message)> LeaveChannelAsync(Client client, ChannelInfo channel)
    {
        try
        {
            await RunWithFloodWaitAsync(() => client.Channels_LeaveChannel(ToInputChannel(channel)));
            return (true, "left");
        }
        catch (RpcException ex)
        {
            return (false, $"Telegram error: {ex.Message}");
        }
    }

    private static string? ExtractInviteHash(string link)
    {
        var idx = link.IndexOf("/+", StringComparison.Ordinal);
        if (idx >= 0) return link[(idx + 2)..];
        idx = link.IndexOf("joinchat/", StringComparison.Ordinal);
        if (idx >= 0) return link[(idx + "joinchat/".Length)..];
        return null;
    }

    private static async Task<T> RunWithFloodWaitAsync<T>(Func<Task<T>> action)
    {
        while (true)
        {
            try
            {
                return await action();
            }
            catch (RpcException ex) when (ex.Message.StartsWith("FLOOD_WAIT", StringComparison.Ordinal) && ex.X > 0)
            {
                var wait = TimeSpan.FromSeconds(ex.X + 1);
                Console.WriteLine($"    Rate limited by Telegram, waiting {wait.TotalSeconds:0}s...");
                await Task.Delay(wait);
            }
        }
    }
}
