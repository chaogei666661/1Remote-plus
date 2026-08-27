# 1Remote

[![version](https://img.shields.io/github/v/release/chaogei666661/1Remote-plus?color=Green&include_prereleases)](https://github.com/chaogei666661/1Remote-plus/releases)
[![issues](https://img.shields.io/github/issues/chaogei666661/1Remote-plus)](https://github.com/chaogei666661/1Remote-plus/issues)
[![license](https://img.shields.io/github/license/chaogei666661/1Remote-plus?color=blue)](https://github.com/chaogei666661/1Remote-plus/blob/main/LICENSE)
![](https://github.com/chaogei666661/1Remote-plus/actions/workflows/build-on-dev-push.yml/badge.svg)

1Remote is a modern personal remote session manager and launcher. It is a single place to manage all your remote sessions supporting a number of different protocols.

> **This is a modified version.**
> Forked from [1Remote/1Remote](https://github.com/1Remote/1Remote) by Shawn Veck, and maintained by [chaogei](https://github.com/chaogei) since 2026.
> It is not affiliated with, nor endorsed by, the upstream project. Please report issues with this build
> [here](https://github.com/chaogei666661/1Remote-plus/issues) rather than upstream.
>
> 本仓库是 1Remote 的修改版本，基于原作者 Shawn Veck 的 [1Remote/1Remote](https://github.com/1Remote/1Remote) 分支而来，由 [chaogei](https://github.com/chaogei) 维护。
> 与上游项目无隶属关系，本版本的问题请提交到[本仓库的 issues](https://github.com/chaogei666661/1Remote-plus/issues)。

## Changes in this fork

- Fixed remote connections failing whenever a proxy was configured, and reworked proxy handling so the
  session keeps its real address instead of being overwritten by the local tunnel endpoint.
- Fixed a set of checked-arithmetic overflows that could crash icon loading, file transfers, credential
  prompts and shortcut creation.
- Reworked the UI around a translucent acrylic design language, replaced font-glyph icons with vector
  paths, and added eight colour themes tuned for the frosted-glass look.

## Security notes

Read this before you point a build at a password store you care about.

**Builds without an encryption salt.** The stored-secret cipher is keyed by a compile-time constant that
CI substitutes from a repository secret. A fork has no access to the upstream secret, so a build produced
without one keeps the placeholder that is published in this repository — a key anybody can read. Such a
build says so on launch and on the About page. **It must not be pointed at a password store created by an
official release**: doing so re-encrypts real secrets under a known key. Build from source with your own
salt if the stored passwords need to be protected.

**What `1Remote.db` protects against.** Casual inspection, and nothing stronger. The class doing the work
is called `UnSafeStringEncipher` for a reason: it is obfuscation with a key compiled into the binary, not
per-user encryption. Anyone holding both the database file and a copy of the program can recover every
password in it. Keep the database where you would keep the passwords themselves.

**What Windows Hello gates.** The second-factor prompt guards actions inside the running app — revealing
or editing a stored credential, for example. It is not a key: the database is enciphered the same way
whether Hello is enabled or not, and turning Hello on does not make an exfiltrated database any harder to
read.

**`cmd://` external secrets are a shell-out.** A password field may hold `cmd://<command line>`, which is
run through `cmd.exe` at connect time and whose output becomes the secret. That makes any writable data
source — a shared SQLite file, a compromised MySQL/PostgreSQL source, a restored backup, an imported
mRemoteNG file — a potential code-execution vector. Each distinct command has to be approved once on this
machine before it will ever run, and approvals are stored locally in `.locality/known_commands.json`; they
never travel with the database. Pre/post-connect scripts are the same class of feature and carry the same
caveat.

**WebDAV backups require HTTPS.** The archive contains the whole configuration, the credential database
included, and the client sends Basic authentication pre-emptively. Plain `http://` is refused unless it is
explicitly enabled in the backup settings, which is only ever reasonable on a loopback or lab endpoint.

**SFTP and FTPS verify host identity** on first use and refuse silently changed identities; accepted
fingerprints live in `.locality/known_hosts.json`.

## Features

- Supports RDP, SSH, VNC, Telnet, (S)FTP, [RemoteApp](https://1remote.github.io/usage/protocol/especial/remoteapp/), [NoMachine and other app](https://1remote.github.io/usage/protocol/especial/app/)
- Quick and convenient remote session launcher (Alt + M)
- Multi-screen and HiDPI RDP connection (Test on **Win10 + 4k monitor *2** RDP TO **Win2016**)
- Detailed connection configuration: tags, icons, colors, connection scripts etc.
- Multiple languages, themes and tabbed interface
- SOCKS4 / SOCKS5 / HTTP CONNECT proxy support for any protocol
- [Import connections from mRemoteNG](https://1remote.github.io/usage/overview/#importing-from-mremoteng)
- Customizable runners, in SFTP \ FTP \ VNC \ etc. protocols, you can replace the internal runner with your favourite tools. [wiki](https://1remote.github.io/usage/protocol/runner/)
- Portable - just unpack and run

## Installation

Latest version: see [Releases](https://github.com/chaogei666661/1Remote-plus/releases). Download the zip, unpack, and run.

Upstream documentation still applies to most features:
[Quick start](https://1remote.github.io/usage/quick-start/)

## Overview

<img src="https://1remote.github.io/img/home_override/hero1.png" width="800" />

<p align="center">
    <img src="https://1remote.github.io/img/home_override/protocols.png" width="400" />
</p>
<p align="center">
    <img src="https://1remote.github.io/img/home_override/hero2.gif" width="400"/>
</p>

<p align="center">
    ↑ Launcher(Alt + M) open RDP connection & resizing
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/multi-screen.jpg" width="500"/>
</p>

<p align="center">
    ↑ RDP with Multi-monitors
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/RemoteApp/demo.jpg" width="800"/>
</p>

<p align="center">
    ↑ RemoteApp via RDP
</p>

## Contributing

Bug reports and ideas are welcome in [issues](https://github.com/chaogei666661/1Remote-plus/issues). If you want to
build from source, see [DEVELOP.md](DEVELOP.md).

## License

GPL-3.0. See [LICENSE](LICENSE).

Copyright (C) Shawn Veck and the 1Remote contributors, for the original work.
Modifications in this fork are copyright (C) chaogei.

## Credits

- Original author: [Shawn Veck](https://github.com/VShawn) — [1Remote/1Remote](https://github.com/1Remote/1Remote)
- <a href="http://www.jetbrains.com/resharper/"><img src="http://www.tom-englert.de/Images/icon_ReSharper.png" alt="ReSharper" width="24" height="24" /></a> ReSharper
