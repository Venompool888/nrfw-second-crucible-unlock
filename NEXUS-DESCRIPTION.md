[b][size=5]Second Crucible Unlock and Encounter Fixes[/size][/b]
[i]An unofficial MelonLoader mod for No Rest for the Wicked — Steam public Build 22928553. 非官方 MelonLoader Mod，适用于《恶意不息》Steam public Build 22928553。[/i]

[b][size=4]📖 图文安装教程 · Screenshot installation guide[/size][/b]
[list]
[*][b]简体中文：[/b][url=https://venompool888.github.io/nrfw-second-crucible-unlock/]https://venompool888.github.io/nrfw-second-crucible-unlock/[/url]
[*][b]English:[/b][url=https://venompool888.github.io/nrfw-second-crucible-unlock/en.html]https://venompool888.github.io/nrfw-second-crucible-unlock/en.html[/url]
[/list]

[quote][size=4][b]遵照教程，你甚至不需要下载n网这个压缩包。[/b][/size]
[size=4][b]Follow the guide and you do not even need to download the archive from Nexus.[/b][/size][/quote]

[line]

[b][size=4]简体中文[/size][/b]

[b]下载[/b]
只需要 [url=https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/CrucibleUnlock.dll]CrucibleUnlock.dll[/url]（0.9.5），不需要解压。想要现成的配置，再下载 [url=https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/MelonPreferences.cfg]MelonPreferences.cfg[/url]。

[b]安装[/b]
1. 安装 MelonLoader 0.7.3。
2. 把 CrucibleUnlock.dll 放到 [code]游戏安装目录\Mods\CrucibleUnlock.dll[/code]。
3. 在 [code]UserData\MelonPreferences.cfg[/code] 的 [code][CrucibleUnlock][/code] 区块里设置：
[code]mode = "runtime-unlock"
guard_broken_warrick_music = true
repair_warrick_phase2_target = true[/code]
4. 如果之前装过别的 Mod，这个 cfg 已经存在——不要覆盖它，只把上面几行合并进去。教程里有《已经有 MelonPreferences.cfg 的玩家》一章专门讲这件事。
5. 解锁只在 Mod 加载期间生效，不会永久完成任务，也不直接修改存档。游戏运行时不要替换 DLL。

[b]功能[/b]
[list]
[*]加载期间临时解锁第二试炼，不永久完成任务，也不直接修改存档。
[*]修复第二献祭盆、地面血路、房间推进、Boss 内容选择与 Warrick 第二阶段目标。
[*]Boss 痕迹掉落可配置（默认 300），第二试炼回声上限提高到 2000，并修正背誓人与堕落外壳的显示。
[/list]

[b]测试范围[/b]
2026-10-05 在 Steam public Build 22928553 上完成过一次九层完整流程，包含预期的七场 Boss 遭遇战、检查点与奖励房。这只是一个版本上的一次测试，其它游戏版本与反复重载场景未验证；版本守卫按精确版本设计，游戏更新后请等待适配版本。

[b]源码[/b]
[url=https://github.com/Venompool888/nrfw-second-crucible-unlock]GitHub 仓库[/url] —— 源码、构建说明、版本记录与代码许可。

[line]

[b][size=4]English[/size][/b]

[b]Download[/b]
You only need [url=https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/CrucibleUnlock.dll]CrucibleUnlock.dll[/url] (0.9.5); no extraction is needed. If you want the ready-made configuration as well, download [url=https://github.com/Venompool888/nrfw-second-crucible-unlock/releases/download/v0.9.5/MelonPreferences.cfg]MelonPreferences.cfg[/url].

[b]Install[/b]
1. Install MelonLoader 0.7.3.
2. Put CrucibleUnlock.dll at [code]game installation directory\Mods\CrucibleUnlock.dll[/code].
3. In the [code][CrucibleUnlock][/code] section of [code]UserData\MelonPreferences.cfg[/code], set:
[code]mode = "runtime-unlock"
guard_broken_warrick_music = true
repair_warrick_phase2_target = true[/code]
4. If you already use other mods, that cfg file exists — do not overwrite it, merge the lines above into it instead. The guide has a chapter, “Already have a MelonPreferences.cfg?”, that covers exactly this.
5. The unlock is active only while the mod runs. It does not permanently complete a quest and does not edit a save. Do not replace the DLL while the game is running.

[b]Features[/b]
[list]
[*]Temporarily unlocks the second Crucible while the mod is loaded. It does not permanently complete a quest or edit a save.
[*]Repairs the second offering bowl sequence, floor blood route, room progression, Boss content selection, and Warrick's phase-two target.
[*]Adds configurable Boss trace rewards (default 300), raises the Second Crucible echo limit to 2000, and corrects the Broken Vow and Wallowing Husk Boss displays.
[/list]

[b]Tested scope[/b]
One complete nine-floor run on 2026-10-05 on Steam public Build 22928553 matched the seven configured Boss encounters, the checkpoint, and the reward room. This is one tested run on one build; other game builds and repeated reload scenarios remain unverified. The build guard is intentionally exact, so wait for an updated mod after a game update.

[b]Source[/b]
[url=https://github.com/Venompool888/nrfw-second-crucible-unlock]GitHub repository[/url] — source, build instructions, version history, and code license.
