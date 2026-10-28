# 2026-10-28 代码审查与整理

审查基线：`f493cc2d7cb97861b5067051040250d0285fec8f`。依据项目 `project-code-review` skill，检查持久化、周期与提醒、窗口交互、启动与托盘生命周期，以及测试和文档的一致性。保留现有具体服务和合理的 code-behind，未引入新的框架或架构层。

## 必要修正

1. 跨文件事件 ID 原先按字符串比较，同一 GUID 的不同文本格式可能绕过冲突检查。现统一按 GUID 值判断，保持原文件中的 ID 文本不变。
2. 保存原先可能覆盖已存在的 `.tmp`，或在保存前置检查失败时删除它。现以 CreateNew 创建临时文件，仅清理本次创建的文件；保存失败仍回滚内存修改并保留原 JSON。
3. 静默启动排队的隐藏操作可能覆盖紧接着发生的手动激活。现检查窗口恢复状态，避免再次隐藏已恢复的主窗口。

三项问题均先通过回归用例复现失败，再修正并验证通过。已同步 README、数据格式说明、CHANGELOG 和项目审查 skill。备注仍严格采用 `note`，旧 `location` 被拒绝，无兼容或自动迁移。

## 验证

- `dotnet build -c Release`：0 警告、0 错误。
- `dotnet run --project Tests/CalendarChecks.csproj -c Release`：完整检查通过，包括新增存储安全检查、85 项事件检查、52 项日历检查，以及全部 2400 个受支持月份的网格检查。
- `Tests/InstanceChecks.ps1`：单实例、关闭后托盘隐藏、两层模态窗口激活、快速重复启动、静默启动与手动恢复、启动期间提前激活均通过。
- `Publish.ps1`：更新框架依赖的单文件 `dist/OneSchedule/OneSchedule.exe`；发布前后对现有日程、备份及设置文件逐一比较 SHA-256，内容未改变。
- `Tests/PortableChecks.ps1`：EXE 独立运行、带空格路径、相邻 data/settings 初始化，以及缺失兼容运行时的原生宿主提示检查通过。
- `Tests/InstanceChecks.ps1 -RuntimeDirectory dist/OneSchedule`：发布版启动、重新激活及托盘检查通过。
- 独立只读复核未发现本次修正引入的可操作问题；最终差异执行 `git diff --check`。

提前激活竞态使用 CalendarChecks.dll 测试宿主精确控制调度顺序，执行应用 DLL 中的启动代码；即使指定 RuntimeDirectory，该用例也不是直接操作单文件 EXE。其它常规发布版启动/重新激活用例直接运行 EXE。

## 清理与边界

仅删除本次生成的 bin/obj、Tests/bin/obj 和 review-*.log；未删除源文件、用户日程、设置或备份。运行程序仅保留在 dist/OneSchedule 下。

未进行真实开机登录、跨显示器 DPI 和声音的完整人工回归；自动化检查不能替代这些实机验证。未发现需要扩大本次范围的架构调整或可选风格修改。
