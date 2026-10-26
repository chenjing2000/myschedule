# 日程编辑与周期存储（用户确认）

- 编辑窗约420×370 DIP，字体14，控件28～30 DIP。类型在首行，阳历/阴历按钮切换同一实际日期，周期无/日/周/月/年。
- 日期合并一行，年/月/日单位在框外。阴历年用二〇二六，月用正、二…十、冬、腊及闰二/闰冬等，日保留初十/廿一。
- 时间、事件、地点、提前提醒、取消/保存的功能保留。后续已确认调整：新增/编辑共用取消与保存，删除统一由当天日程列表提供。
- 日期为起始日；无只发生一次，日/周间隔1/7个实际日，月/年按所选历法匹配；月重复包含闰月，缺失日期跳过。年重复匹配同一月份、日期与闰月属性；阳历2月29日非闰年跳过。
- 标准UTF-8 JSON：schemaVersion=1，events包含id/name/location/calendar/startDate(year/month/day/isLeapMonth)/time/recurrence/reminderMinutes/lastRemindedOccurrenceDate。历法solar/lunar，周期none/daily/weekly/monthly/yearly，HH:mm本地时间，提醒0～99分钟。
- 重复规则只存一条，日历/列表/提醒复用同一发生日期判断。提醒记录是该次发生的阳历日期，跨午夜提前提醒也用目标日期。
- 破坏性升级，不迁移旧格式。拒绝旧/损坏/未知版本文件，并阻止覆盖；保留用户文件及备份。首次无文件可新增保存。
- 本地project-code-review skill改为WPF职责边界，去除EnglishNotes专有规定；保持简单直接，不引入框架或无收益抽象。

选择标准JSON而非YAML/数据库，使用.NET内置System.Text.Json，避免依赖。保持窗口/具体服务直接协调。
