# TG Channels Migrator

A small interactive C# console tool for your own Telegram account:

1. **Migrate** — logs a second Telegram account in and joins it to every channel/group
   your main ("source") account is a *member* of.
2. **Delete** — makes the source account leave every channel/group it's a *member* of.

Channels/groups the source account **created** are ignored by both operations: ownership
is never transferred (Telegram's API doesn't support that), and they're never left or
deleted — they're simply left alone.

It talks to Telegram directly over MTProto (the same protocol the official apps use)
via [WTelegramClient](https://github.com/wiz0u/WTelegramClient) — there's no bot involved,
because bots can't see or manage a user's channel list.

> **Use responsibly.** This automates your own personal account(s). Only run it against
> accounts you own or are explicitly authorized to manage, and be aware Telegram may rate-limit
> or flag accounts that join/leave many chats in a short time. The tool automatically waits out
> `FLOOD_WAIT` errors, but for large numbers of channels a run can take a while.

## Step-by-step: running it end to end

This walks through a full session from a clean checkout to a finished migration,
with the exact commands and the prompts you'll see.

**1. Install prerequisites and get API credentials**

- Install the [.NET 8 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should print `8.x`).
- Go to https://my.telegram.org, log in with the phone number of the account you're
  migrating **from**, open **API development tools**, and create an app. Copy the
  `api_id` and `api_hash` shown there.

**2. Clone and build**

```bash
git clone https://github.com/RostilKant/tg-chanels-migrator.git
cd tg-chanels-migrator
dotnet build
```

**3. Export your API credentials (optional but avoids retyping them)**

```bash
export TG_API_ID=123456
export TG_API_HASH=abcdef0123456789abcdef0123456789
```

**4. Launch the tool**

```bash
dotnet run --project src/TgChannelsMigrator
```

You'll land on the menu:

```
1) Log in source account (the one you're migrating FROM)
2) List channels on the source account
3) Migrate: log in a second account and join it to your non-owned source channels
4) Delete: leave every source channel EXCEPT ones you created
5) Quit
Choose an option:
```

**5. Log in the source account — choose `1`**

```
--- Logging in [source] account ---
[source] Phone number, international format (e.g. +15551234567): +15551234567
[source] Login code Telegram just sent you: 12345
[source] Two-factor password (leave blank if none):
[source] Logged in as Jane Doe (@janedoe, id=123456789)
```

The session is saved to `sessions/source.session`, so you won't have to log in again
on future runs unless the session expires.

**6. Check what was found — choose `2`**

Prints a table of every channel/group, flagging which ones you created
(`OWNED = yes`) so you know what Migrate/Delete will and won't touch.

**7. Migrate to a second account — choose `3`**

```
Ignoring 2 channel(s)/group(s) you created - ownership isn't transferred, so they're left as-is:
  - My Own Channel
  - My Own Group

About to join a second account to 14 channel(s)/group(s) the source account is a member of (not owner).
Continue? (yes/no): yes

--- Logging in [target] account ---
[target] Phone number, international format (e.g. +15551234567): +15559876543
[target] Login code Telegram just sent you: 54321
[target] Logged in as Jane's Second Account (@janedoe2, id=987654321)

[1/14] Joining "Some Public Channel"... OK (joined via public username)
[2/14] Joining "Private Group I'm in"... SKIPPED (private channel and source account can't export an invite link (not admin/creator) - join it manually)
...
Migration finished: 11 joined, 3 skipped.
```

Any "SKIPPED" channel needs to be joined manually with the target account (you weren't
an admin there, so the tool couldn't generate an invite link for it).

**8. Clean up the source account — choose `4`**

```
This will make the source account LEAVE 14 channel(s)/group(s) it doesn't own:
  - Some Public Channel
  - Private Group I'm in
  ...

It will KEEP 2 channel(s)/group(s) you created:
  - My Own Channel
  - My Own Group

Type "delete 14" to confirm: delete 14
[1/14] Leaving "Some Public Channel"... OK
...
Done: left 14 channel(s), 0 failed.
```

You must type the confirmation phrase exactly (including the count) or nothing happens.
This step is independent of Migrate — you can run it before, after, or without ever migrating.

**9. Quit — choose `5`** (or just re-run `dotnet run --project src/TgChannelsMigrator` later;
logged-in sessions persist between runs).

## Login sessions

Each account's login session is cached in `sessions/source.session` and `sessions/target.session`
(re-running the tool won't ask you to log in again unless the session expires or is revoked).
These files are equivalent to being logged into that Telegram account — **never commit or share
them**; `.gitignore` already excludes the whole `sessions/` folder.

## Logs

The console only shows this tool's own menu and progress output. WTelegramClient's own
low-level connection chatter (`Sending MsgContainer`, `Receiving RpcResult`, ...) is written
instead to `logs/telegram.log` (created on startup, one line per event with a timestamp and
level). Useful if you need to troubleshoot a connection issue, otherwise safe to ignore or
delete — it's gitignored like `sessions/`.

## Project layout

```
src/TgChannelsMigrator/
  Program.cs                 CLI menu / orchestration
  TelegramAccountSession.cs  Login flow (console prompts) + session handling
  ChannelService.cs          List / join / leave channels, FLOOD_WAIT handling
  ChannelInfo.cs             Plain data record for a channel
  TelegramLogger.cs          Redirects WTelegramClient's protocol logs to logs/telegram.log
```
