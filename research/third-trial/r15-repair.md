# R15：第三试炼楼层 HUD 与敌人声线修复候选

本次按用户“尝试修复，但是修完不启动游戏”要求准备离线候选，不安装、不启动或操作游戏，不改源存档。开发分支 `feature/third-trial-hud-voice-repair-20261010` 从最新 `origin/main` 的 `7db4e4a06d04cb1f6ca1d8cfa1a3aaceaeb66a08` 新建，带入同一第三试炼已试玩的 R14 功能。正式 0.9.17 的固定修复和仅 `boss_trace_drop_amount` 设置保留；没有合并到 main。游戏目录仍保留 R14 DLL，SHA256 `39835bd42d16aff082e1c0f79afa0443ea4d1b3384659b3f405d7972ff4cc3be`。

## 修复依据和行为

R14 日志直接记录 1365 次 `PlayerCrucibleHUD.UpdateCurrentFloor` 越界，随后引擎因异常过多终止。日志没有拾武器动作记录，因此不能把拾取认定为根因。官方 HUD 只有 11 个文案和图标，原生按 `CompletedFloorsForPlayer + SkippedFloors` 先读取文案，再检查奖励房。第三路线源条目数为 11，但生成列表可插入休息房，不能用源条目数推断实际索引。

R15 仅在源 playlist GUID 精确匹配私有第三试炼、当前 `ViewFrame.GetQtn` 与本地绑定角色可核验时处理该 HUD 实例。依据实际生成列表和跳层数计算有界容量，上限 64；保留原始 11 条消息，使用 Unity `LocalizedMessage` 副本补齐之后的 Roman 标签和 12 种语言。没有官方图标的楼层隐藏自有图标，不复用错误图标。普通试炼、外部替换字段、归池和重置按拥有关系恢复；负数、溢出、无效私有状态只抑制当前楼层 UI 调用，不改模拟进度或原始资产。

原生 `LocalizedMessage.GetLocalizedRawText` 直接读取对应语言字段；正常本地化模式不要求把新增 Id 写入全局表。外部开启调试模式时仍遵照官方 Id 显示规则，没有修改全局模式或缓存。证据见工作区 `artifacts/third-launch/r15-hud/`。

巨锤审判者和死潮壮汉的原模型自定义声线会优先于新身份的官方声音池；共用模型也可能把上一次选择的声线留到下一次绑定。R15 在 `FulfillVoiceAssignment` 之前，使用既有 `EnemyRestorationOwnerScope` 核验当前私有敌人、动态资源和原生附件身份，从有效 `CharacterType` 的官方池选择现有开关。随后保留原生分配和 `SetAudioAttributes` 路径。每次绑定保持稳定选择；解绑、归池、销毁只还原本模块仍拥有的字段；外部接管后保留抑制标记，后续回调不覆盖它。

声音修复不创建录音、不强制播放事件、不改预制体或全局声音池。`StopWithoutUnityReads` 本身不访问原生对象，后续安全生命周期回调归还拥有的赋值。原生视图若在资源重置时直接失效且没有生命周期回调，不能安全追写其指针；这一限制保留在候选证据中。官方组件 AOT 使用已核验的 Has #605433、Get #599544，不使用有问题的 `Frame.TryGet` out 参数桥。声音证据见工作区 `artifacts/third-launch/r15-voice/`。

## 启动及旧功能兼容

保留第三入口门控 → 完整 Ritual 早期预编译 → HUD／声音完整 helper 预编译 → 恢复模块安装的顺序。Ritual 晚期 Install 继续重复完整预编译，HUD／声音局部 Install 也重复预编译；不会以短回调代替主体检查。所有新增 detour 回调为短 `NoInlining` 方法。

R14 keeper 武器三参数 `SetParent(parent, false, false)` 保持，原双参数 throwing stub 不再使用。既有十种敌人、十个竞技房、第二路线推进／奖励、官方普通刷怪器和弩枪 0.3.3 不改变。没有混入其他未合并任务功能。

## 离线验证与未验收项

使用 `verify-third-trial.ps1` 构建核心和音频插件，执行 20 个托管测试套件及 4 个 PE 元数据门控。套件链接实际生产源码，Unity／Quantum 边界使用受控假实现，没有加载或执行游戏 DLL。原始无修复基线能复现 HUD 越界及声线问题；新增测试包括奖励访问顺序、普通范围、外部接管、资源生命周期、停止和重复绑定。

扩展 hook 门控核对 21 个回调和 18 个原生目标，扫描供应的 17 个生成依赖以拦截 throwing unstripping 方法；另用元数据工具核对已知 unsafe interop。早期与晚期预编译顺序通过静态 IL 门控。构建、逐项输出及最终候选源码哈希保存在 `artifacts/third-launch/r15-repair/` 和候选包 `verification.json`。

候选包为 `dist/third-trial-20261010-r15`，仅用于后续用户自行测试；R14 和正式 0.9.17 文件均保留。离线通过不能证明本次实际 detour 注册、楼层显示、声音事件播放或音库驻留成功，也不能确认存在额外剧情对白。日志中的 `scissorAttack` 重复键和单次 `DamageBuildup` 空引用尚无关联证据，本次没有混入推测性修复。
