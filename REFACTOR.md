# OneSchedule 重构记录

本文件记录历史重构；2026-10-26的新周期和破坏性JSON格式以README及docs/data-format.md为准。

重构原则：保持已经确认的 UI、`MySchedule.json` 格式、农历规则和用户交互行为不变；优先降低重复、减少碎片文件，并把相似功能集中在同一模块。

## 第一轮：结构清理（v0.2.0-r1）

- `Colors.xaml + Styles.xaml` 合并为 `Resources/Theme.xaml`。
- Calendar 的小型数据/事件类型并入 `CalendarView.xaml.cs`。
- `AlarmService` 与任务栏闪烁实现归入提醒模块。
- `ScheduleStore + ScheduleJson` 归并为单一日程数据模块。
- 多份版本日志合并为一个 `CHANGELOG.md`。

结果：C#/XAML 文件从 23 个降到 18 个，源码从 2136 行降到 2116 行。

## 第二轮：日程业务收口（v0.2.0-r2）

- 新增统一 `ScheduleService`，集中加载、查询、新增、修改、删除、保存和 ID 生成。
- 删除 `ScheduleList` 辅助类。
- `ReminderService` 直接依赖统一的 `ScheduleService`，删除重复的日程同步流程。
- 删除收益很低的 `MainViewModel`，简单 UI 状态回到主窗口，业务仍留在 Service。

结果：C#/XAML 文件从 18 个降到 17 个，源码从 2116 行降到 2052 行。

## 第三轮：Calendar 数据模板化（v0.2.0-r3）

- 删除 `DateCellControl.xaml/.cs` 两个文件。
- 日期格改为 `CalendarView.xaml` 内部的 `ItemsControl + UniformGrid + DataTemplate`。
- 阳历、阴历、周末、非本月、今天高亮、事项三角标记全部改为 XAML Style/DataTrigger。
- 日期格点击和键盘 Enter/Space 统一由 Button 自带交互处理。
- 提醒深浅闪烁改为 CalendarView 的单一依赖属性，XAML 触发器统一响应，不再逐个遍历日期控件并手工修改颜色。
- 星期标题从 code-behind 动态生成改为 XAML 静态布局。
- ToolTip 改为日期 ViewModel 的派生属性，不再由独立 DateCell 控件构造。

第三轮结果：C#/XAML 文件从 17 个降到 15 个，源码从 2052 行降到 2002 行。

## 第四轮：日程编辑逻辑收敛（v0.2.0-r4）

- `ScheduleEditWindow` 的新增/编辑初始化合并到单一 `FillForm` 流程，删除两个平行初始化分支。
- 年/月/日联动统一经过 `RefreshDateOptions`，闰月状态和天数刷新不再由多个入口分别拼接。
- 年份裁剪、两位时间格式、编辑态显示年份、时间解析等重复细节收敛为小型私有函数。
- 阳历/阴历日期校验统一由 `CreateDate` 完成，继续保持旧 JSON 对阴历“保存数字年月日”的兼容规则。
- 提醒身份比较集中到 `SameReminderIdentity`，保留“日期/时间/历法/闰月/事件类型未改变时继承 LastRemindedYear”的原行为。
- 提醒分钟键盘与粘贴校验保持原规则：仅数字、最多两位、保存时再次校验 0～99。
- 不新增文件，不引入 ViewModel/Behavior 框架，相关编辑逻辑仍集中在同一个窗口文件中。

第四轮结果：C#/XAML 文件保持 15 个，源码从 2002 行降到 1970 行；`ScheduleEditWindow.xaml.cs` 从 242 行降到 210 行。

## 累计变化（v0.1.11 → v0.2.0-r4）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1970 | -166（-7.8%） |
| `ScheduleEditWindow.xaml.cs` | 242 | 210 | -32（-13.2%） |

当前阶段已达到“减少文件碎片”的主要目标。后续更适合继续处理业务模型中的字符串状态、日期/时间重复解析，以及 `ScheduleService.cs` 内数据持久化部分的可读性，而不是继续机械拆文件。



## 第五轮：模型状态强类型化

- `ScheduleItem.Type` 使用 `ScheduleType`，业务层不再依赖 `"Daily"/"Birth"` 字符串比较。
- `ScheduleItem.EventTime` 使用 `TimeOnly?`；`Time` 仅作为 UI/JSON 的只读 `HH:mm` 文本投影。
- JSON 解析继续接受原有 `EventType` 和 `Time` 格式，序列化输出保持不变。
- 日程排序、提醒计算和编辑窗口的身份比较均直接使用强类型时间。
- 删除与 README 重复的旧 `MySchedule.md` 文档。

## 第六轮：日期 occurrence 规则集中（v0.2.0-r6）

- 从 `ScheduleItem` 删除 `EffectiveDate` / `GetEventDateTime`，模型不再自行推导实际发生日期。
- 日期 occurrence 规则统一集中到 `Services/ScheduleService.cs` 内的 `ScheduleOccurrence`：普通阳历、普通阴历、阳历生日、阴历生日、闰月生日和 2 月 29 日生日只保留一个权威实现。
- `ScheduleService.GetForDate`、Calendar 日期映射、Reminder 到期判断全部改用同一个 occurrence 入口，消除三处未来可能产生规则分叉的风险。
- 编辑窗口中的闰月生日显示年份、日期创建校验和提醒身份比较也归入同一 occurrence 模块。
- 不新增源码文件，继续遵循“相近功能集中”的原则；JSON 字段和现有数据完全不变。

第六轮结果：C#/XAML 文件保持 15 个；`ScheduleItem` 从 97 行降到 57 行，`ScheduleEditWindow.xaml.cs` 从 208 行降到 190 行。由于集中后的 occurrence 模块增加了明确的公共规则入口，C#/XAML 总行数从 1966 小幅增加到 1981 行（+15），换取日期规则单一来源和更低耦合。

## 累计变化（v0.1.11 → v0.2.0-r6）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1981 | -155（-7.3%） |
| `ScheduleItem` | 约 110 行（早期） | 57 行 | 明显纯化 |
| `ScheduleEditWindow.xaml.cs` | 242 | 190 | -52（-21.5%） |

## 第七轮：ScheduleService 内部整理（v0.2.0-r7）

- 保持 `ScheduleService.cs` 单文件，不为持久化、迁移、JSON 再拆出小文件；按“业务操作 / 文件与 ID / 旧格式迁移 / occurrence / 可读 JSON”组织内部结构。
- 新增统一 `Commit` 回滚入口，新增、修改、删除不再各自重复“修改集合 → 保存 → 失败恢复”流程。
- 原子保存集中到 `ReplaceFile`，临时文件清理集中到 `TryDelete`，减少 `Save` 内嵌套层级。
- ID 解析和格式化集中为 `ParsePositiveId` / `FormatId`，`NextId` 与 ID 修复共用同一规则。
- JSON 行预处理、错误抛出、整数解析和属性输出均使用统一辅助函数，错误行号与现有手工可读格式保持不变。
- occurrence 中普通/闰月阴历的有效天数检查合并为一条路径，继续保持原行为。
- 不增加源码文件，不改变界面，不改变 `MySchedule.json` 字段或旧数据迁移行为。

第七轮结果：C#/XAML 文件保持 15 个，源码从 1981 行降到 1978 行；`ScheduleService.cs` 从 413 行降到 409 行。行数下降不大，但重复事务、文件替换、ID 和 JSON 辅助逻辑都获得单一入口，主流程嵌套更浅。

## 累计变化（v0.1.11 → v0.2.0-r7）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1978 | -158（-7.4%） |
| `ScheduleService.cs` | r6: 413 | 409 | -4 |
| `ScheduleEditWindow.xaml.cs` | 242 | 190 | -52（-21.5%） |


## 第八轮：提醒状态流程集中（v0.2.0-r8）

- 将“声音 / 日历深浅闪烁 / 窗口恢复与置顶 / 任务栏闪烁 / 提醒活动状态”统一收进 `ReminderService`，主窗口不再分别维护这些底层提醒状态。
- `MainWindow` 删除 `_alarm`、`_flashTimer`、`_reminding`、`_flashDark` 以及闪烁 Tick 处理，只保留提醒文字、提醒条显示/隐藏等真正属于界面的工作。
- `ReminderService` 提供 `IsAlertActive` 与 `DismissAlert()` 作为提醒呈现状态的单一入口；开始提醒只由到期检测流程内部调用。
- `Dispose()` 统一完成提醒关闭、扫描计时器停止和事件解绑，窗口关闭不再逐项清理声音和闪烁状态。
- `AlarmService` 和 `WindowTaskbarFlasher` 继续与提醒模块放在同一文件中，并收窄为 `internal`，不增加源码文件。
- 保持提醒声音、500 ms 深浅切换、任务栏闪烁、窗口恢复/置顶、点击关闭等现有行为不变。

第八轮结果：C#/XAML 文件仍为 15 个；`MainWindow.xaml.cs` 从 168 行降到 135 行。为了把完整提醒生命周期从窗口收口到 Reminder 模块，`ReminderService.cs` 从 188 行增加到 235 行；项目源码总量由实际统计的 1977 行变为 1991 行（+14），换取主窗口状态变量和跨文件 Start/Stop 协调的大幅减少。

## 累计变化（v0.1.11 → v0.2.0-r8）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1991 | -145（-6.8%） |
| `MainWindow.xaml.cs` | 早期约 192 | 135 | -57（约 -29.7%） |
| `ScheduleEditWindow.xaml.cs` | 242 | 190 | -52（-21.5%） |
| `ScheduleItem` | 早期约 110 | 57 | 明显纯化 |

## 第九轮：主窗口月份状态与刷新链路集中（v0.2.0-r9）

- 将原先独立的 `_year` / `_month` 两个可变状态合并为单一 `_displayMonth`，避免年份和月份在事件处理中出现短暂不同步。
- 年份与月份 ComboBox 共用一个 `PeriodCombo_SelectionChanged` 入口，不再维护两套几乎相同的 SelectionChanged 处理函数。
- 新增统一 `NavigateTo(year, month)`，今天按钮和下拉框切换均通过同一月份导航路径。
- 月份变化时统一执行“更新农历标题 → 重建日历”；仅日程数据变化时只重建日历，不重复计算月份标题。
- 刷新日程与日程编辑后的 Calendar 更新使用同一个 `RebuildCalendar()`，减少多处直接 `Calendar.Rebuild(...)` 调用。
- 不新增源码文件，不改变界面、日历显示、提醒行为或 `MySchedule.json` 格式。

第九轮结果：C#/XAML 文件仍为 15 个；`MainWindow.xaml.cs` 从 135 行降到 128 行；C#/XAML 总行数从 1991 行降到 1985 行。更重要的是主窗口月份状态从两个整数缩减为一个原子状态，年月选择和“今天”导航共用一条刷新链路。

## 累计变化（v0.1.11 → v0.2.0-r9）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1985 | -151（-7.1%） |
| `MainWindow.xaml.cs` | 早期约 192 | 128 | -64（约 -33.3%） |
| `ScheduleEditWindow.xaml.cs` | 242 | 190 | -52（-21.5%） |
| `ScheduleItem` | 早期约 110 | 57 | 明显纯化 |

## 第十轮：日程弹窗交互结果收敛（v0.2.0-r10）

- `DayEventsWindow` 不再向主窗口公开 `Changed` 属性，也不再把“完成按钮的 DialogResult”与“数据是否真的变化”混在一起；新增 `ShowForDate(...)` 作为唯一入口，直接返回本次弹窗是否修改了日程。
- 主窗口打开当日事项后只判断一个布尔结果；只有实际新增、编辑或删除成功时才重建 Calendar，单纯打开后点击“完成”不再触发无意义刷新。
- `ScheduleEditWindow` 不再公开 `Result` / `DeleteRequested` 两个可变状态；新增 `ShowEditor(...)`，以一个可空结果一次性返回“保存/删除/取消”结果。
- 当日事项中的新增、编辑、删除统一经过 `ApplyChange(...)`：保存失败提示、Changed 状态和列表刷新只保留一套流程。
- 关闭当日事项窗口（包括右上角 X）后仍能正确把“是否发生过修改”返回主窗口，不依赖 WPF `DialogResult` 传递业务状态。
- 不新增源码文件，不改变窗口布局、按钮行为、日程保存格式或提醒逻辑。

第十轮结果：C#/XAML 文件仍为 15 个；源码从 1985 行降到 1972 行（-13）。`DayEventsWindow.xaml.cs` 从 100 行降到 89 行，`MainWindow.xaml.cs` 从 128 行降到 123 行。`ScheduleEditWindow.xaml.cs` 因加入统一弹窗入口从 190 行变为 193 行，但删除了跨窗口的两个公开状态属性，交互协议更集中。

## 累计变化（v0.1.11 → v0.2.0-r10）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1972 | -164（-7.7%） |
| `MainWindow.xaml.cs` | 早期约 192 | 123 | -69（约 -35.9%） |
| `DayEventsWindow.xaml.cs` | r9: 100 | 89 | -11（-11%） |
| `ScheduleEditWindow.xaml.cs` | 242 | 193 | -49（-20.2%） |
| `ScheduleItem` | 早期约 110 | 57 | 明显纯化 |

## 第十一轮：全项目重复逻辑与样式收尾（v0.2.0-r11）

- 这一轮不再改变主要架构，只删除能够静态确认的重复和职责残留。
- `ScheduleItem` 从 57 行进一步降到 30 行：JSON 字符串解析移入 `ScheduleJson`，Tooltip 展示格式移入 Calendar 层；模型只保留日程数据、强类型时间以及 UI/JSON 兼容的 `Time` 文本投影。
- 当前 JSON 与旧格式迁移共用 `ScheduleJson.ParseTime`，避免模型层承担数据格式解析职责。
- `DialogActionButtonStyle` 统一 6 个弹窗操作按钮的 104×38、16 号字和居中设置；`RoundedTextBoxStyle` 统一编辑窗口输入框的 32 高和 16 号字。
- 删除未引用的 `AccentHoverColor` / `AccentHoverBrush`，并整理此前资源合并留下的缩进。
- 没有新增源码文件，没有改变 `MySchedule.json` 字段、Calendar 行为、提醒行为或窗口尺寸。

第十一轮结果：C#/XAML 文件仍为 15 个；源码从 1972 行降到 1967 行。单纯按行数下降并不大，但 `ScheduleItem` 已经从早期约 110 行缩到 30 行，数据模型与 JSON/展示职责的边界更加清楚。

## 累计变化（v0.1.11 → v0.2.0-r11）

| 指标 | 重构前 | 当前 | 变化 |
|---|---:|---:|---:|
| C#/XAML 文件 | 23 | 15 | -8（-34.8%） |
| C#/XAML 总行数 | 2136 | 1967 | -169（-7.9%） |
| `MainWindow.xaml.cs` | 早期约 192 | 123 | -69（约 -35.9%） |
| `DayEventsWindow.xaml.cs` | r9: 100 | 89 | -11（-11%） |
| `ScheduleEditWindow.xaml.cs` | 242 | 193 | -49（-20.2%） |
| `ScheduleItem` | 早期约 110 | 30 | 约 -73% |


## v0.2.0-r12：公历 / 阴历双向日期编辑

- ComboBox 保持 WPF 默认外观，不做圆角模板替换；ToolTip 使用原生 `Border.CornerRadius` 统一为圆角提示框。
- “今日日程”窗口标题区改为两行：第一行显示 `阳历日期（农历月日）`，例如 `2026年8月27日（农历七月十五）`；第二行显示“今日日程”。
- 新增/编辑日程窗口增加独立“阳历日期”和“阴历日期”两行，每行均由年、月、日三个 ComboBox 表示。
- 阳历年月日改变时自动换算并刷新阴历；阴历年月日改变时自动换算并刷新阳历，使用同步保护避免 SelectionChanged 循环触发。
- 阴历月份下拉框直接显示 `正月 / … / 闰X月`，不再使用独立“闰月”复选框；阴历日下拉显示 `初一 … 三十`。
- “类型”中的阳历/阴历继续只决定日程以后按哪一种历法保存和重复，不再决定当前界面显示哪一套日期；两套日期始终表示同一天。
- 生日事件的提醒身份比较忽略“参考年份”，避免双向日期换算选择新的参考年份时无意义地重置 `LastRemindedYear`。
- `MySchedule.json` 字段和格式保持不变。
