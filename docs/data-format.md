# 日程 JSON 契约（schemaVersion 3）

程序只读取标准 UTF-8、版本3格式。日程在 EXE 同目录 data 的第一层 JSON 文件中（扩展名不区分大小写），不读取 .bak、不递归子目录。目录缺失会创建；没有 JSON 时创建有效空白 MySchedule.json，已有坏文件不覆盖。按文件名 OrdinalIgnoreCase 排序，新增写入第一份；编辑、删除和提醒完成记录写回原文件。

这是破坏性升级；应用不读取版本1、2或旧 location 字段，也不静默迁移。读取错误、跨文件 ID 冲突及外部修改阻止覆盖；其它正常源文件仍可独立使用。用户数据、备份和临时文件由 .gitignore 排除，构建和发布不会复制或打包这些文件。

## 字段

根对象必须包含 schemaVersion: 3 和 events 数组。事件必须包含全部下列字段；未知字段、缺失字段或错误类型拒绝读取。

| 字段 | 含义 |
| --- | --- |
| id | 有效非空 GUID，跨文件按 GUID 值唯一；编辑不变 |
| name / note | 非空事件名称；备注可为空字符串；location 不兼容 |
| calendar | solar 或 lunar |
| startDate | 所选历法的 year/month/day/isLeapMonth；年份1901～2100，日期实际有效 |
| time | 严格本地时间 HH:mm，无秒 |
| recurrence | none/daily/weekly/monthly/yearly |
| repeatCount | 1～999，包含起始日；none 为1，年默认100，日/周/月默认2 |
| reminderMinutes | 0～99，新增默认30，0为准点 |
| reminderCount | 1～99，默认3；不同于事件的 repeatCount |
| reminderIntervalMinutes | 1～99，默认3；相邻两轮开始时间的间隔 |
| reminderDurationMinutes | 1～99，默认1；每轮声音和闪烁持续时间 |
| completedReminderDates | 已提前结束或已记录完成的阳历发生日期数组 yyyy-MM-dd；无记录为 []；不可重复，必须属于事件实际发生日期，最多 repeatCount 个 |

多轮时 interval 不能小于 duration；只有一轮时 interval 不参与计算。四个输入只接受 ASCII 数字，提前分钟允许0，其余不允许0。

## 历法与周期

阴历年月日独立存储，不能构造为阳历 DateTime；闰月标志必须实际有效。二〇二六、冬、闰腊、廿一等仅用于显示。

起始日之前不发生；daily/weekly 按实际1/7天。monthly 按所选历法同日并包含普通月和闰月；yearly 保持同月、同日、同闰月属性。不存在日期跳过、不改月末、不消耗次数；2月29日只在实际存在时发生。日历、列表、提醒共用 ScheduleOccurrence，编辑/删除作用于整条规则，有限次数用尽后保留事件记录。

## 多轮提醒、续接与标记

每次发生的首次开始时间 = 事件本地日期时间 - reminderMinutes。第 n 轮在首次开始 + (n-1)×interval 开始，持续 duration 分钟，结束边界不包含在响铃时段内。

默认09:00事件的提醒为08:30～08:31、08:33～08:34、08:36～08:37。间隔从上次开始算，既定时段不会因启动延迟或重启延长。错过整轮则跳过，不集中补响；重启处于某轮内只运行到原定结束，静默间隔等待下一轮。旧版三分钟补发窗口不再用于新提醒。

时间安排由已保存的日期、时间和参数确定，无需保存重复的倒计时/轮次计数器。点击界面提前结束所有当前响铃的发生日期并写入 completedReminderDates；静默等待的其它事件不取消。多个事件共用声音/闪烁，界面列出所有当前响铃项目。退出程序只停止输出，不等于取消；下一次启动继续剩余时段。单个周期事件的多天提醒组可以重叠，状态按各阳历发生日期区分。

整组结束后标记才前进；间隔中仍保持当前未结束项目。全部结束保留最后事件颜色，提示和列表不删项目。记录保存失败时回滚集合、保留外部源文件；本地取消仍会静音并保留重试意图。修复源文件并刷新后，在提醒规则未改变时重试。如果未成功保存就退出，重启后的取消保证不能成立，界面会显示保存错误。自然到期由时间判断，因此停机越过全部时段也不会补响。

名称/备注修改保留完成状态并复制集合；日期、历法、周期、时间、任一提醒参数改变则清空。只改事件 repeatCount 时保留仍在新范围内的完成日期，剔除越界日期。

## 保存与显式升级

保存先校验，使用同目录排他创建 .tmp 并原子替换，保留滚动 .bak。已有临时文件不覆盖，失败只清理本次创建的临时内容。源文件从加载后被外部修改时禁止覆盖，须刷新。

关闭正式程序，再运行开发检查项目中的显式工具：

```powershell
dotnet run --project Tests/CalendarChecks.csproj -c Release -- --upgrade-v2 "dist/OneSchedule/data/MySchedule.json"
```

版本2升级保留事件 ID、内容、历法、周期次数及原提前分钟，新增参数填3/3/1，旧已提醒日期转成完成日期，防止重放。含 location、缺失/未知字段或无效数据拒绝升级。版本1也可显式使用 --upgrade-v1，按原约定补默认周期次数，清除落在有限范围外的旧提醒记录。

工具验证结果后原子替换，原始字节保留为 .schema-v1-<id>.bak 或 .schema-v2-<id>.bak，不覆盖滚动备份；已有升级临时文件不覆盖。对版本3再次运行只校验、不改写。运行程序本身不自动执行该工具。

## 验证

运行 dotnet run --project Tests/CalendarChecks.csproj -c Release。覆盖新参数、精确时段、重启、跨日重叠、取消范围、保存失败/刷新重试、严格格式及备份升级，并包含原有历法、周期和 WPF 检查。示例见 [完整示例](examples/schedules.json) 和 [空文件](examples/empty-schedules.json)，不随应用自动载入。设置仍在 EXE 旁 OneSchedule.settings.json，不属于日程数据。
