namespace TgChannelsMigrator;

public sealed record ChannelInfo(
    long Id,
    long AccessHash,
    string Title,
    string? Username,
    bool IsCreator,
    bool CanInvite,
    bool IsMegagroup);
