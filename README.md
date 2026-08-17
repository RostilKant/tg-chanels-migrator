# TG Channels Migrator

A small interactive C# console tool for your own Telegram account:

1. **Migrate** — logs a second Telegram account in and joins it to every channel/group
   your main ("source") account is currently a member of.
2. **Delete** — makes the source account leave every channel/group it *doesn't* own,
   keeping only the ones it created.

It talks to Telegram directly over MTProto (the same protocol the official apps use)
via [WTelegramClient](https://github.com/wiz0u/WTelegramClient) — there's no bot involved,
because bots can't see or manage a user's channel list.

> **Use responsibly.** This automates your own personal account(s). Only run it against
> accounts you own or are explicitly authorized to manage, and be aware Telegram may rate-limit
> or flag accounts that join/leave many chats in a short time. The tool automatically waits out
> `FLOOD_WAIT` errors, but for large numbers of channels a run can take a while.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- A Telegram `api_id` / `api_hash` pair from https://my.telegram.org → **API development tools**
  (free, tied to your phone number, needed to talk to Telegram's API at all)

## Setup

```bash
git clone <this repo>
cd tg-chanels-migrator
export TG_API_ID=123456          # optional: skips the prompt
export TG_API_HASH=abcdef0123...  # optional: skips the prompt
dotnet run --project src/TgChannelsMigrator
```

If you don't set `TG_API_ID` / `TG_API_HASH`, the tool will just ask for them the first time
it needs them.

## Usage

The tool shows a menu:

```
1) Log in source account (the one you're migrating FROM)
2) List channels on the source account
3) Migrate: log in a second account and join it to all source channels
4) Delete: leave every source channel EXCEPT ones you created
5) Quit
```

Typical flow:

1. **Log in source account** — enter the phone number, the login code Telegram sends you,
   and your 2FA password if you have one. This is the account whose channels you're working with.
2. **List channels** — sanity-check what was found, and which ones you're marked as the
   creator of (`OWNED = yes`).
3. **Migrate** — prompts you to log in a *second* account (the destination). For each channel:
   - Public channels are joined directly by username.
   - Private channels are joined via an invite link that the source account exports —
     this only works if the source account is the creator or an admin with "invite users"
     rights on that channel. Channels the source can't export an invite for are skipped
     and printed out so you can join them manually.
4. **Delete** — lists every channel/group the source account is a member of but did **not**
   create, and every one it will keep (the ones it created). You must type an exact
   confirmation phrase (`delete N`) before anything is left. This only ever calls "leave chat" —
   it never deletes a channel outright, and channels you created are never touched by this option.

## Login sessions

Each account's login session is cached in `sessions/source.session` and `sessions/target.session`
(re-running the tool won't ask you to log in again unless the session expires or is revoked).
These files are equivalent to being logged into that Telegram account — **never commit or share
them**; `.gitignore` already excludes the whole `sessions/` folder.

## Project layout

```
src/TgChannelsMigrator/
  Program.cs                 CLI menu / orchestration
  TelegramAccountSession.cs  Login flow (console prompts) + session handling
  ChannelService.cs          List / join / leave channels, FLOOD_WAIT handling
  ChannelInfo.cs             Plain data record for a channel
```
