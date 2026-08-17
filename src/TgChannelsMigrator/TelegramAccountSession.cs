using System.Text;
using TL;
using WTelegram;

namespace TgChannelsMigrator;

/// <summary>
/// Wraps a logged-in WTelegramClient session for one Telegram account ("source" or "target"),
/// prompting on the console for whatever WTelegramClient needs (api_id/hash, phone, code, 2FA password).
/// </summary>
public sealed class TelegramAccountSession : IDisposable
{
    public string Role { get; }
    public Client Client { get; }
    public User Me { get; }

    private TelegramAccountSession(string role, Client client, User me)
    {
        Role = role;
        Client = client;
        Me = me;
    }

    public static async Task<TelegramAccountSession> LoginAsync(string role)
    {
        Directory.CreateDirectory("sessions");
        var sessionPath = Path.Combine("sessions", $"{role}.session");

        string? ConfigProvider(string what) => what switch
        {
            "api_id" => RequireEnvOrPrompt("TG_API_ID", "Telegram api_id (get one at https://my.telegram.org)"),
            "api_hash" => RequireEnvOrPrompt("TG_API_HASH", "Telegram api_hash (get one at https://my.telegram.org)"),
            "phone_number" => Prompt($"[{role}] Phone number, international format (e.g. +15551234567): "),
            "verification_code" => Prompt($"[{role}] Login code Telegram just sent you: "),
            "password" => PromptMasked($"[{role}] Two-factor password (leave blank if none): "),
            "session_pathname" => sessionPath,
            "first_name" => Prompt($"[{role}] First name (only asked if this creates a new account): "),
            "last_name" => Prompt($"[{role}] Last name (optional): "),
            _ => null,
        };

        Console.WriteLine();
        Console.WriteLine($"--- Logging in [{role}] account ---");
        var client = new Client(ConfigProvider);
        var user = await client.LoginUserIfNeeded();
        Console.WriteLine($"[{role}] Logged in as {user.first_name} {user.last_name} (@{user.username}, id={user.id})");
        return new TelegramAccountSession(role, client, user);
    }

    private static string RequireEnvOrPrompt(string envVar, string label)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : Prompt($"{label}: ");
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine()?.Trim() ?? "";
    }

    private static string PromptMasked(string label)
    {
        Console.Write(label);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        return sb.ToString();
    }

    public void Dispose() => Client.Dispose();
}
