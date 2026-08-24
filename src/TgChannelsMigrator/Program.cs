using TgChannelsMigrator;

const string logPath = "logs/telegram.log";
TelegramLogger.ConfigureFileLogging(logPath);

Console.WriteLine("=== Telegram Channels Migrator ===");
Console.WriteLine("Moves your channel memberships to another Telegram account,");
Console.WriteLine("and can clean up channels you don't own.");
Console.WriteLine();
Console.WriteLine("You'll need an api_id/api_hash from https://my.telegram.org (\"API development tools\").");
Console.WriteLine("Set them once via TG_API_ID / TG_API_HASH env vars, or you'll be prompted for them.");
Console.WriteLine($"(Telegram's own connection logs are written to {logPath}, not the console.)");
Console.WriteLine();

var joinDelay = GetDelayFromEnv("TG_JOIN_DELAY_SECONDS", defaultSeconds: 15);
var leaveDelay = GetDelayFromEnv("TG_LEAVE_DELAY_SECONDS", defaultSeconds: 5);
Console.WriteLine($"Delay between joins: {joinDelay.TotalSeconds:0}s, between leaves: {leaveDelay.TotalSeconds:0}s");
Console.WriteLine("(override with TG_JOIN_DELAY_SECONDS / TG_LEAVE_DELAY_SECONDS env vars)");
Console.WriteLine();

TelegramAccountSession? source = null;

try
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Menu:");
        Console.WriteLine("  1) Log in source account (the one you're migrating FROM)");
        Console.WriteLine("  2) List channels on the source account");
        Console.WriteLine("  3) Migrate: log in a second account and join it to your non-owned channels");
        Console.WriteLine("  4) Delete: leave every non-owned channel (groups are never touched)");
        Console.WriteLine("  5) Quit");
        Console.Write("Choose an option: ");
        var choice = Console.ReadLine()?.Trim();

        switch (choice)
        {
            case "1":
                source?.Dispose();
                source = await TelegramAccountSession.LoginAsync("source");
                break;

            case "2":
                if (!EnsureLoggedIn(source)) break;
                await ListChannelsAsync(source!);
                break;

            case "3":
                if (!EnsureLoggedIn(source)) break;
                await MigrateAsync(source!, joinDelay);
                break;

            case "4":
                if (!EnsureLoggedIn(source)) break;
                await DeleteNonOwnedAsync(source!, leaveDelay);
                break;

            case "5":
            case "q":
            case "quit":
            case "exit":
                return;

            default:
                Console.WriteLine("Unrecognized option.");
                break;
        }
    }
}
finally
{
    source?.Dispose();
}

static bool EnsureLoggedIn(TelegramAccountSession? source)
{
    if (source != null) return true;
    Console.WriteLine("Log in the source account first (option 1).");
    return false;
}

static async Task<List<ChannelInfo>> ListChannelsAsync(TelegramAccountSession source)
{
    Console.WriteLine("Fetching channel list...");
    var channels = await ChannelService.GetChannelsAsync(source.Client);

    if (channels.Count == 0)
    {
        Console.WriteLine("No channels found.");
        return channels;
    }

    Console.WriteLine();
    Console.WriteLine($"{"OWNED",-6} {"KIND",-9} {"TITLE",-45} USERNAME");
    foreach (var c in channels)
    {
        var owned = c.IsCreator ? "yes" : "no";
        var kind = c.IsMegagroup ? "group" : "channel";
        var username = c.Username != null ? "@" + c.Username : "(private)";
        Console.WriteLine($"{owned,-6} {kind,-9} {Truncate(c.Title, 45),-45} {username}");
    }

    Console.WriteLine();
    Console.WriteLine($"Total: {channels.Count}  |  Created by you: {channels.Count(c => c.IsCreator)}  |  Not owned by you: {channels.Count(c => !c.IsCreator)}");
    return channels;
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static async Task MigrateAsync(TelegramAccountSession source, TimeSpan joinDelay)
{
    var allChannels = await ChannelService.GetChannelsAsync(source.Client);
    var owned = allChannels.Where(c => c.IsCreator).ToList();
    var groups = allChannels.Where(c => !c.IsCreator && c.IsMegagroup).ToList();
    var candidates = allChannels.Where(c => !c.IsCreator && !c.IsMegagroup).ToList();

    if (owned.Count > 0)
    {
        Console.WriteLine($"Ignoring {owned.Count} channel(s)/group(s) you created - ownership isn't transferred, so they're left as-is:");
        foreach (var c in owned)
            Console.WriteLine($"  - {c.Title}");
        Console.WriteLine();
    }

    if (groups.Count > 0)
    {
        Console.WriteLine($"Ignoring {groups.Count} group(s) - Migrate only handles channels, groups are left as-is:");
        foreach (var c in groups)
            Console.WriteLine($"  - {c.Title}");
        Console.WriteLine();
    }

    if (candidates.Count == 0)
    {
        Console.WriteLine("No non-owned channels to migrate.");
        return;
    }

    Console.WriteLine($"About to join a second account to up to {candidates.Count} channel(s) the source account is a member of (not owner).");
    Console.Write("Continue? (yes/no): ");
    if (!IsYes(Console.ReadLine())) { Console.WriteLine("Cancelled."); return; }

    using var target = await TelegramAccountSession.LoginAsync("target");

    if (target.Me.id == source.Me.id)
    {
        Console.WriteLine("The target account is the same as the source account - nothing to migrate.");
        return;
    }

    var progress = MigrationProgress.Load(target.Me.id);
    var alreadyJoined = candidates.Count(c => progress.IsJoined(c.Id));
    var toMigrate = candidates.Where(c => !progress.IsJoined(c.Id)).ToList();

    if (alreadyJoined > 0)
        Console.WriteLine($"Skipping {alreadyJoined} channel(s) this target account already joined in a previous run.");

    if (toMigrate.Count == 0)
    {
        Console.WriteLine("Nothing left to migrate - the target account is already in every eligible channel.");
        return;
    }

    Console.WriteLine($"Joining {toMigrate.Count} channel(s)...");

    int ok = 0, failed = 0;
    for (var i = 0; i < toMigrate.Count; i++)
    {
        var channel = toMigrate[i];
        Console.Write($"[{i + 1}/{toMigrate.Count}] Joining \"{channel.Title}\"... ");
        var (success, message) = await ChannelService.JoinChannelAsync(source.Client, target.Client, channel);
        Console.WriteLine(success ? $"OK ({message})" : $"SKIPPED ({message})");
        if (success)
        {
            ok++;
            progress.MarkJoined(channel.Id);
        }
        else
        {
            failed++;
        }

        // Be polite to Telegram's rate limits between joins.
        if (i < toMigrate.Count - 1)
            await Task.Delay(joinDelay);
    }

    Console.WriteLine();
    Console.WriteLine($"Migration finished: {ok} joined, {failed} skipped.");
}

static async Task DeleteNonOwnedAsync(TelegramAccountSession source, TimeSpan leaveDelay)
{
    var channels = await ChannelService.GetChannelsAsync(source.Client);
    var toLeave = channels.Where(c => !c.IsCreator && !c.IsMegagroup).ToList();
    var toKeep = channels.Where(c => c.IsCreator).ToList();
    var groups = channels.Where(c => !c.IsCreator && c.IsMegagroup).ToList();

    if (toLeave.Count == 0)
    {
        Console.WriteLine("Nothing to do - no non-owned channels found (groups are never touched by this option).");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"This will make the source account LEAVE {toLeave.Count} channel(s) it doesn't own:");
    foreach (var c in toLeave)
        Console.WriteLine($"  - {c.Title}");
    Console.WriteLine();
    Console.WriteLine($"It will KEEP {toKeep.Count} channel(s)/group(s) you created:");
    foreach (var c in toKeep)
        Console.WriteLine($"  - {c.Title}");
    if (groups.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"It will also leave {groups.Count} non-owned group(s) untouched (Delete only handles channels):");
        foreach (var c in groups)
            Console.WriteLine($"  - {c.Title}");
    }

    Console.WriteLine();
    Console.Write($"Type \"delete {toLeave.Count}\" to confirm: ");
    var confirmation = Console.ReadLine()?.Trim();
    if (confirmation != $"delete {toLeave.Count}")
    {
        Console.WriteLine("Confirmation didn't match. Cancelled.");
        return;
    }

    int ok = 0, failed = 0;
    for (var i = 0; i < toLeave.Count; i++)
    {
        var channel = toLeave[i];
        Console.Write($"[{i + 1}/{toLeave.Count}] Leaving \"{channel.Title}\"... ");
        var (success, message) = await ChannelService.LeaveChannelAsync(source.Client, channel);
        Console.WriteLine(success ? "OK" : $"FAILED ({message})");
        if (success) ok++; else failed++;

        if (i < toLeave.Count - 1)
            await Task.Delay(leaveDelay);
    }

    Console.WriteLine();
    Console.WriteLine($"Done: left {ok} channel(s), {failed} failed.");
}

static TimeSpan GetDelayFromEnv(string envVar, int defaultSeconds)
{
    var raw = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, out var seconds) && seconds >= 0)
        return TimeSpan.FromSeconds(seconds);
    return TimeSpan.FromSeconds(defaultSeconds);
}

static bool IsYes(string? input) =>
    input?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true ||
    input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
