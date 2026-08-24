using System.Text.Json;

namespace TgChannelsMigrator;

/// <summary>
/// Tracks which channels a given target account has already been successfully joined to,
/// persisted to disk under progress/migrate-{targetUserId}.json. Lets a restarted Migrate
/// run pick up where it left off instead of retrying channels that already succeeded
/// (e.g. after a long FLOOD_WAIT gets interrupted).
/// </summary>
public sealed class MigrationProgress
{
    private readonly string _path;
    private readonly HashSet<long> _joinedChannelIds;

    private MigrationProgress(string path, HashSet<long> joinedChannelIds)
    {
        _path = path;
        _joinedChannelIds = joinedChannelIds;
    }

    public static MigrationProgress Load(long targetUserId)
    {
        Directory.CreateDirectory("progress");
        var path = Path.Combine("progress", $"migrate-{targetUserId}.json");

        HashSet<long> ids = new();
        if (File.Exists(path))
        {
            try
            {
                ids = JsonSerializer.Deserialize<HashSet<long>>(File.ReadAllText(path)) ?? new HashSet<long>();
            }
            catch (JsonException)
            {
                // Corrupt/partial progress file - start fresh rather than fail the whole run.
                ids = new HashSet<long>();
            }
        }

        return new MigrationProgress(path, ids);
    }

    public bool IsJoined(long channelId) => _joinedChannelIds.Contains(channelId);

    public void MarkJoined(long channelId)
    {
        if (_joinedChannelIds.Add(channelId))
            File.WriteAllText(_path, JsonSerializer.Serialize(_joinedChannelIds));
    }
}
