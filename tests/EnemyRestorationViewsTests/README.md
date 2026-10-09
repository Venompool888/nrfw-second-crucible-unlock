# R9 EntityView 根回归

本 harness 编译真实 `EnemyRestorationViews.cs`，只用纯 managed fake；不引用、加载或执行游戏/Unity DLL。

R8 冻结源为 `R8EnemyRestorationViews.snapshot`。同一 harness 对 R8 首次失败在 `Expected failure: missing legal HumanoidEnemyView root fails closed: weapon`：旧代码验证 GUID/name 后即接受武器根。新生产源 **127 checks passed**，包括武器根、只在子对象有正确组件、任务 NPC 根、合法 HumanoidEnemyView、官方衣装 settings 共享只读、异步根加载、加载失败、pool 替换、source settings 指针替换，以及既有 canonical identity/GUID/Path 不写、不新增第二 EntityViewData、三缓存碰撞和回滚。

```powershell
../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationViewsTests -c Release --property:EnemyViewsSource=R8EnemyRestorationViews.snapshot
../crucible-mod/.dotnet/dotnet.exe run --project tests/EnemyRestorationViewsTests -c Release
```

固定 RED/GREEN 输出在 `artifacts/third-launch/r9-views-frozen-r8-red.txt` 与 `r9-views-green.txt`。离线 fake 检查只验证接线和 readiness 编排，不是本轮游戏画面/战斗测试。
