# WinRedis 交付说明与 PRD 完成清单

- 交付日期：2026-10-29
- 运行环境：Windows / .NET 10 (`net10.0-windows`) / WPF
- 验证实例：本机 Redis 5.0.14.1（127.0.0.1:6379），Demo 数据位于 DB9；DB0 的 25 个业务 Key 未改动
- 构建：`dotnet build WinRedisClient.slnx -c Release`，0 警告 / 0 错误
- 测试：`dotnet test WinRedisClient.slnx -c Release`，132 通过 / 0 失败

## 一、交付范围

本阶段以 PRD 的 V1.0 核心开发体验为主，并提前完成 V1.1 的监控、SlowLog、Key 对比、BigKey、Stream、命令面板与暗色主题。所有数据均来自真实 Redis 连接；未接入演示数据、未保留无实现的空壳入口。

| 模块 | 状态 | 说明 |
| --- | --- | --- |
| 设计系统与 Shell | ✅ 完成 | 侧栏导航、顶部工具栏、状态栏、通知、折叠、明暗主题、统一控件样式 |
| 连接管理 | ✅ 完成 | 新建/编辑/测试/连接/断开/删除、左侧按自定义分组以目录形式展示（标题行折叠/展开、状态持久化）、拖拽到分组标题/连接卡片快速移动分组、右键菜单快速移动到已有分组/预置分组（生产/预发/测试/开发）/新建分组、连接编辑对话框分组快速选择（已有分组 + 预置分组 + 自定义输入）、连接警告色标记、复制脱敏配置、搜索、环境标签、DB 选择、连接后自动批量加载（默认开启）、SSH 密码/私钥文件认证 |
| 数据浏览 | ✅ 完成 | 顶部连接信息栏（连接名/地址/复制/DB 切换/各库 Key 数预览/状态/延迟/刷新）、SCAN 分页、本地模糊过滤（默认）/全匹配/数据库模糊（性能确认）、防抖 + Enter、类型/TTL 筛选、列表/目录树（文件夹优先、记住展开状态）、自动批量加载与续载 |
| String 编辑器 | ✅ 完成 | UTF-8 文本、GBK/GB18030 自动回退解码、超长内容默认自动换行、可关闭换行启用横向滚动条、JSON 对象/数组默认自动格式化、只读预览、草稿还原、保存并保留 TTL |
| Hash 编辑器 | ✅ 完成 | 字段新增/修改/删除，危险删除确认 |
| List 编辑器 | ✅ 完成 | 按索引追加、编辑、删除 |
| Set 编辑器 | ✅ 完成 | 成员新增/编辑/删除 |
| ZSet 编辑器 | ✅ 完成 | 查看/调整 Score，成员新增/删除 |
| Stream 查看器 | ✅ 完成 | 消息列表、追加消息、删除消息（均需确认），不支持消息内容编辑 |
| TTL / 重命名 / 删除 | ✅ 完成 | 预设与自定义 TTL、PERSIST、RENAMENX、完整 Key 名删除确认；详情头部直接提供重命名/TTL/删除按钮 |
| 实例监控 | ✅ 完成 | INFO、commandstats、keyspace、SlowLog、自动采样、暂停、Sparkline |
| 命令终端 | ✅ 完成 | 执行结果/错误/耗时、风险等级、历史、L0 只读标识、L2/L3 二次确认 |
| Key 对比 | ✅ 完成 | 跨连接/跨 DB、类型不一致提示、字段级差异、复制/导出 |
| BigKey 分析 | ✅ 完成 | SCAN + MEMORY USAGE、类型/体积/元素数、进度、取消、CSV 导出、打开 Key；点击行右侧内嵌只读预览 |
| 最近与历史 | ✅ 完成 | 最近访问、命令历史、复制/填入终端/单条删除/清空 |
| 全局命令面板 | ✅ 完成 | Ctrl+K、页面/最近/Key 分组、键盘选择、Enter 打开、Esc 关闭 |
| 设置 | ✅ 完成（有缺口） | 8 套主题配色（含 5 套低饱和浅色主题）即时应用并持久化、界面字体大小（小/标准/大，315 处字号即时缩放）、中英文界面（默认中文，切换后重启生效）、关闭行为（每次询问 / 最小化到托盘 / 直接退出，可记住选择）、分隔符、目录视图、默认 DB、监控间隔、历史开关/上限、隐私清理、配置导出、恢复默认 |
| 国际化与开源交付 | ✅ 完成 | 839 条界面文案中英文资源（以中文原文为键，缺失回退中文）、设置页语言切换与重启引导；MIT 许可证、中英文 README、Inno Setup 安装脚本与打包文档 |
| 品牌与图标 | ✅ 完成 | 按提供的 SVG 生成多尺寸 `.ico` 与 PNG；exe、窗口、任务栏、标题栏、设置页图标一致；标题栏与“关于”卡片展示 WinRedis wordmark，明暗主题自适应 |

## 二、PRD 验收清单

### 20.1 主框架

- [x] 窗口调整大小时布局不重叠
- [x] 左侧导航可折叠，折叠状态写入本地设置
- [x] 当前连接、环境标签与 DB 在侧栏、页头和状态栏可见
- [x] 页面切换保持各自 ViewModel 状态，不串线
- [x] 提供 8 套主题配色：浅色、深色、跟随系统，以及海盐、奶糖、森林、繁花、微风 5 套低饱和浅色主题；设置页使用带配色预览的卡片选择器
- [x] 所有主题下文字与状态均有足够对比度（修复了目录树前景色问题）
- [x] 无边框窗口最大化时限制在当前显示器工作区，任务栏不被遮挡
- [x] 数据浏览页不显示独立的“数据浏览”标题，改由顶部连接信息栏承担页头职责；其他页面继续保留标题、DB 与刷新入口
- [x] 全局搜索保留在标题栏（未因设计图未展示而删除）
- [x] 标题栏、窗口、任务栏与 exe 文件图标均来自同一份 SVG 品牌图形；标题栏与设置页展示 WinRedis wordmark，深色主题下自动切换文字颜色
- [x] 左侧导航使用与页面语义匹配的矢量图标（列表、显示器、终端、对比、图表、历史、齿轮），替换原先的文字占位；折叠后仅显示图标并保留 Tooltip

### 20.2 连接管理

- [x] 新建、编辑、测试、连接、断开、删除
- [x] 认证/网络失败显示归一化错误；失败状态在列表中标记
- [x] 切换连接先断开旧会话并重置浏览状态，不展示旧 Key
- [x] 生产环境红色“生产”标签 + 执行层额外确认
- [x] 设置页导出连接配置默认不含密码；侧栏单连接“复制配置”同样脱敏
- [x] 密码输入默认掩码，DPAPI 加密落盘（`JsonConnectionStore`）
- [x] 支持 Standalone + TLS + SSH 隧道；SSH 支持密码或私钥文件（OpenSSH/PEM，可从文件系统选择，口令 DPAPI 加密）；未展示不可用的 Sentinel/Cluster 选项
- [x] 连接可设置自定义分组，左侧以目录形式按分组展示并显示组内数量；分组标题行带折叠/展开箭头，折叠状态持久化到 settings.json（重启恢复）；留空归入“未分组”，搜索时忽略折叠展示全部匹配项
- [x] 连接可设置警告色（无/黄/橙/红/紫/蓝/绿），左侧卡片用色条 + 警告图标 + Tooltip 标记，仅影响本地展示
- [x] 分组与警告色随连接配置持久化；复制/导出配置包含这两项且仍不含任何密码或 SSH 凭据
- [x] 连接卡片可拖拽到分组标题或其他连接卡片上快速移动分组；拖拽时目标分组高亮，失败则回滚分组
- [x] 右键连接卡片可在打开前构建的菜单中快速“移动到已有分组/预置分组（生产/预发/测试/开发）/新建分组…/移出分组”，当前分组带勾选标记，移动成功后列表、分组计数立即更新并持久化
- [x] 连接编辑对话框“分组”字段带快速选择按钮：下拉列出已有分组 + 预置常用分组，仍支持自定义输入
- [x] 界面支持中文 / English 切换（默认中文）：设置 → 外观 → 界面语言，保存后提示重启；重启后全界面（导航、侧栏、各页面、对话框、提示与校验文案）切换语言
- [x] 全部界面文案经 `Loc.T` / `{loc:Tr}` 资源化（841 条），缺失英文自动回退中文；单元测试校验资源完整性与占位符一致性

### 20.3 Key 浏览

- [x] Key 搜索、类型筛选、TTL 筛选
- [x] 默认“模糊匹配”只在已加载 Key 中做纯前端过滤，不访问 Redis
- [x] “全匹配”直接读取目标 Key 元数据，复杂度 O(1)
- [x] 开启“数据库模糊”前二次确认性能风险，确认后才使用 SCAN MATCH；取消则回到本地过滤
- [x] SCAN 迭代（COUNT 500，游标续扫；目录树已启用虚拟化），未调用 `KEYS *`
- [x] 连接/切换 DB 后默认自动批量加载（首批目标 2,000 个 Key），可取消并继续加载下一批
- [x] 列表与目录树均启用虚拟化，配合“加载下一批”避免一次渲染全部 Key
- [x] 列表/树形切换；目录树按分隔符聚合，且每一级都优先显示文件夹、再显示普通 Key
- [x] 目录树默认折叠所有文件夹；展开/折叠状态按连接 + DB 持久化，重新加载、切换连接或重启后恢复
- [x] 文件夹与 Key 各自按名称升序排列（`KeyTreeBuilderTests` 覆盖根级与子级）
- [x] Key 不存在、断连、扫描失败均给出真实状态
- [x] 新建 Key 默认禁止覆盖；覆盖需显式勾选并确认
- [x] 连接名称、地址、复制地址、DB 切换、连接状态、延迟与刷新集中在数据浏览顶部连接栏；DB 切换后列表与状态栏同步
- [x] 展开 DB 下拉时用 `INFO keyspace` 一次读取各库 Key 数量，显示为 `DB 0 · 26` 形式；不切换当前 DB、不调用 `KEYS *`
- [x] 左侧“连接管理”保留新增、搜索、连接/断开、编辑、删除；已连接卡片显示“断开”
- [x] 筛选栏在窄窗口自动换行；左右面板按 35/65 分配并保留最小宽度，1080 宽下操作可用
- [x] 切换主题或页面往返后，当前连接、DB、筛选条件与选中 Key 保持不变

### 20.4 编辑器

- [x] String 可查看、复制、编辑、保存、还原草稿；保存保留 TTL
- [x] 超长 String 默认自动换行，不溢出编辑框；取消“自动换行”后恢复单行显示并出现横向滚动条
- [x] 合法 JSON 对象/数组在打开时自动缩进格式化，不标记为未保存；截断预览与二进制不格式化
- [x] Hash 新增/修改/删除字段（实测 HSET/HDEL）
- [x] List 按索引查看、编辑、追加、删除（实测 LSET/RPUSH/LDEL）
- [x] Set 添加/编辑/删除成员（实测 SADD/SREM）
- [x] ZSet 查看 Score、调整 Score、添加/删除成员（实测 ZADD/ZREM）
- [x] Stream 提供消息查看、追加、删除（实测 XADD/XDEL）
- [x] 切换 Key/切换 DB/切换连接/关闭窗口前检查未保存草稿
- [x] 详情头部提供重命名、设置 TTL、删除 Key 按钮，无需右键
- [x] 保存失败保留草稿，编辑器显示错误
- [x] 成功状态均由实际 Redis 操作结果确认

### 20.5 TTL 与危险操作

- [x] 设置 TTL / 永不过期；预设与自定义，校验非法输入
- [x] 重命名使用 `RENAMENX`，目标存在时拒绝覆盖
- [x] 删除确认展示完整 Key 名与连接/DB，提示不可撤销
- [x] 生产环境删除、覆盖、批量等高危操作二次确认（需输入确认文本）
- [x] 风险分类 L0–L3 在 `RedisService` 执行路径生效，不只做前端提示

### 20.6 监控与 CLI

- [x] 指标来自真实 INFO / COMMANDSTATS / KEYSPACE / SLOWLOG
- [x] “暂无数据/不支持”与真实 0 区分；SlowLog 为空显示说明
- [x] 采样失败保留最近一次成功数据并显示最近刷新时间
- [x] Sparkline 仅使用本次会话真实采样（不伪造历史）
- [x] CLI 显示结果、错误、耗时与风险等级
- [x] 命令历史可清空，认证类命令不记录
- [x] `FLUSHDB` 等 L3 命令必须手动输入命令名才能执行（截图 21）

### 20.7 高级功能

- [x] Key 对比处理类型不一致（显示类型差异，不强行比较内容）
- [x] BigKey 使用 SCAN + MEMORY USAGE，显示进度，可取消，不阻塞
- [ ] 数据迁移：未实现（见未完成项；界面未提供假入口）
- [x] 最近访问与命令历史可关闭/清空；清空前确认

## 三、实测记录（本机 Redis）

| 验证项 | 结果 |
| --- | --- |
| 连接测试 | `连接成功 · Redis 5.0.14.1 · 2 ms` |
| 新建 Key | `qa:verify:demo = hello from WinRedis UI`，真实写入 |
| TTL 设置 | `PEXPIRE` 后 `TTL=300s`，界面提示“TTL 已更新” |
| 字符串保存保留 TTL | 初始 86400s，保存后 `TTL=86060s`，值已更新 |
| 重命名 | `qa:string:ttl` → `qa:string:renamed`，TTL 保留，旧 Key 不存在 |
| 删除 Key | 二次确认后删除，`EXISTS=0`，DB Key 数同步下降 |
| Hash | HSET/HSET 修改/HDEL 均成功，详情保持打开 |
| List | RPUSH/LSET/LDEL 成功，元素恢复原值 |
| Set | SADD/SREM 成功，SCARD 恢复 5 |
| ZSet | ZADD/更新 Score/ZREM 成功，Score 恢复 1870 |
| Stream | XADD 新增消息、XDEL 删除消息，XLEN 恢复 3 |
| 危险命令 | `FLUSHDB` 弹出 L3 确认，未输入命令名时执行按钮禁用；取消后 DB 仍为 14 个 Key |
| 跨 DB 对比 | DB9 vs DB8 `shop:product:2001`：新增 1、删除 0、修改 2 |
| BigKey | 扫描 14 个 Key，按体积排序，最大 `shop:events:orders` 792 B |
| 配置导出 | 导出 JSON `passwordIncluded=false`，无 password/SSH password 字段 |
| 全局搜索 | Ctrl+K → 输入 product → 3 个 Key 结果 → Enter 打开详情 |
| 全匹配搜索 | DB14 2,600 个 Key 中精确查找未加载的 `qa:bulk:2599`，只返回该 Key，标记“精确匹配” |
| 本地模糊搜索 | 输入 `sys` 后显示 23 / 已加载 25 个 Key，状态明确标注“本地过滤”，未发起 Redis 扫描 |
| 数据库模糊确认 | 切换到“数据库模糊”先弹性能警告（含已加载数量与 SCAN 风险）；取消后回退本地过滤 |
| 数据库模糊搜索 | 确认后执行 SCAN MATCH，结果 23 个并在扫描完成时结束；返回本地模式会恢复原有已加载集合 |
| 批量加载 | 切换至 DB14（2,600 Keys）后自动批量加载 2,001 个 Key，约 4 秒；可取消并续载 |
| 连接默认项 | 编辑连接显示“连接后自动批量加载 Key（默认开启）”且默认勾选 |
| 批量开关持久化 | 取消勾选保存后 `connections.json` 的 `AutoLoadKeys=false`；重新勾选后恢复为 `true` |
| SSH 私钥认证 UI | 连接表单可切换“密码 / 私钥文件”；私钥模式提供文件路径、浏览按钮与可选口令 |
| SSH 私钥持久化 | 保存后 `SshAuthMode=PrivateKey`、私钥路径明文、口令 DPAPI 加密（`connections.json` 已验证） |
| SSH 私钥错误处理 | 测试连接时对无效私钥报“SSH 私钥无法读取…请确认 OpenSSH/PEM 与口令”，不产生半开隧道 |
| 连接分组 | 临时设置“本地环境 / 生产集群 / 未分组”三组后，左侧以目录形式显示分组标题（折叠箭头 + 组内数量），搜索可匹配分组名；验证后已恢复原连接配置 |
| 分组折叠/展开 | 点击分组标题折叠/展开“我的”分组，卡片随即收起/恢复；折叠状态写入 settings.json `CollapsedConnectionGroups` 并在重启后恢复；空分组不显示标题行；搜索时忽略折叠展示全部匹配（单元测试覆盖） |
| 分组快速选择 | 编辑“200 Redis”时点击分组字段的快速选择按钮，下拉展示已有分组“我的”+ 预置“生产/预发/测试/开发”；仍可自由输入（截图 59） |
| 右键移动分组 | 右键连接卡片，菜单在打开前动态构建：设置分组… / 当前勾选的已有分组 + 预置分组 / 新建分组… / 移出分组；实测点击“生产”后 prestg 连接立即归入该分组并持久化，验证后已恢复原配置（截图 60） |
| 快速移动分组 | 拖拽事件与目标分组高亮已实测；分组变更持久化、空分组映射与保存失败回滚由单元测试覆盖；右键“移出分组”实测写回配置 |
| 连接警告色 | 本地连接绿色、生产连接红色时，卡片左侧色条 + 警告图标 + Tooltip 正常显示；未连接/已连接状态不受影响 |
| 分组与警告色持久化 | 分组名和警告色随连接配置保存/读取；复制与设置页导出包含这两项，仍不含 Redis/SSH 密码（单元测试覆盖） |
| 界面语言切换 | 设置 → 外观 → 界面语言选择 English 后立即写入 settings.json，并弹出“重启后生效，是否现在重启”确认；确认后应用自动重启并以英文界面运行（导航、侧栏、设置、监控、命令终端、Key 对比全部英文，截图 61–65）；验证后已恢复中文默认并重启确认 |
| 英文界面完整性 | 英文模式下逐页截图核对：无中文残留（除分组名等用户数据）；界面文案资源 841 条，占位符与中文键完全一致（单元测试校验） |
| 开源交付 | MIT LICENSE、README.md（中文）与 README.en.md（英文）、Inno Setup 安装脚本 tools/installer/WinRedis.iss、docs/安装包制作.md |
| 关闭窗口 | 首次点击关闭（窗口 X 与任务栏右键关闭均验证）弹出“最小化到托盘 / 直接退出 / 取消”；选托盘后窗口隐藏、进程继续运行，可从托盘图标恢复；选直接退出或勾选“记住我的选择”后按设置执行（Ask→弹窗、Exit→直接退出，均已实测）；修复了首次关闭在 Closing 事件内同步调用 Close() 触发“窗口关闭期间”异常的问题（改为 Dispatcher 异步关闭 + 重入保护） |
| SSH 配置脱敏 | 复制/导出包含 `authMode` 与 `privateKeyPath`，不包含私钥口令（单元测试覆盖） |
| 目录优先排序 | 文件夹节点始终排在普通 Key 前，同级各自按名称升序；单元测试覆盖根级与子级 |
| JSON 默认格式化 | 打开 `qa:json:demo` 后自动缩进展示，状态“JSON 已自动格式化”，保存按钮保持禁用 |
| 超长 String 换行 | 4.5 KB 测试字符串默认自动换行、不溢出编辑框；取消“自动换行”后单行显示并出现横向滚动条，测试后已删除临时 Key |
| 文件夹默认折叠 | 连接 DB0 后仅显示 `sys_config (9)`、`sys_dict (14)`、`test (2)` 三个收起节点 |
| 展开状态记忆 | 展开 `sys_config` 后写入 settings.json；刷新、重新连接后保持展开，其他文件夹仍折叠 |
| 详情快捷操作 | 详情头部直接显示重命名 / TTL / 删除；实测 TTL 与重命名对话框、删除确认均可打开 |
| BigKey 内嵌预览 | 点击候选行右侧显示 Key 类型/TTL/大小/数量与 Hash 字段预览，无需跳转数据浏览 |
| GBK 值显示 | `shop:product:2002` 的 GBK 字节解码为“机械件盘 87件”，编码标记 GBK，无替换字符（单元测试覆盖） |
| 主题 | 8 套主题（浅色/深色/跟随系统/海盐/奶糖/森林/繁花/微风）通过预览卡片选择，立即生效并写入 settings.json；重启后保持；逐套实机截图 |
| 界面字体大小 | 小（90%）/ 标准（100%）/ 大（115%）选择后全局即时生效并写入 settings.json；重启后保持；抽查数据浏览、Key 详情与连接对话框在 115% 下无文字截断 |
| 数据浏览改版 | 连接名/地址/复制/DB/状态/延迟/刷新收进顶部连接栏；DB0 → DB9 后列表与状态栏同步；1080 宽时筛选栏换行、面板 35/65 分配，操作可用 |
| 数据库数量预览 | 展开 DB 下拉时通过 `INFO keyspace` 一次读取全部 DB，显示 `DB 0 · 26`、`DB 9 · 14`、`DB 15 · 1` 等真实数量；切换 DB 后当前项数量即时更新；未连接时不可展开 |
| 改版状态保持 | 连接 DB9 并选中 `shop:product:2001` 后切到设置再返回、切换浅色/深色/森林主题，连接、DB 与选中 Key 均保持 |
| 网络与 DB 切换 | DB9 → DB8 → DB9，Key 数与页头/状态栏同步 |
| 最大化窗口 | 屏幕 2560×1440、任务栏 48px；最大化后窗口为 0,0,2560×1392，状态栏完整可见，任务栏未被遮挡 |

## 四、未完成项与已知差异

1. **数据迁移（PRD 12，V1.2）未实现**：没有迁移入口，避免空壳。需要源/目标、冲突策略、预演与回滚设计。
2. **监控趋势图为基础 Sparkline**：没有时间轴、Tooltip、1h/6h/24h 历史范围与持久化；当前只展示本次会话采样，符合“不伪造历史”。
3. **Stream 编辑器不完整**：不支持消息内容展开、消费者组、一次 XADD 多字段；仅支持单字段追加与消息删除。
4. **设置项缺口**：窗口/面板尺寸设置、语言切换、独立操作审计日志未实现；UI 字体大小已完成（小/标准/大）。
5. **全局搜索范围**：仅当前 DB 的 Key + 最近访问 + 页面跳转；未提供跨连接搜索与命令执行分组。
6. **列表右键菜单**：提供复制 Key 名、重命名、TTL、删除；未提供“复制 Value”与批量操作（详情页可复制 Value）。
7. **未保存切换对话框**：提供“丢弃并继续 / 返回编辑”，未提供“保存并切换”第三选项。
8. **连接配置导入未实现**：仅提供脱敏导出与复制配置。
9. **Key 对比**：Stream 内容暂不参与比较；与 PRD 中“后续支持”一致。
10. **生产保护实测限制**：本地只有开发环境；生产确认逻辑由同一执行层代码路径覆盖，未连接真实生产实例演练。
11. **SSH 私钥端到端连接**：本机无权限启动 sshd，未做真实 SSH 服务器连通演练；已验证私钥读取失败的真实报错、表单持久化与脱敏。
12. **GBK 写回**：读取会自动识别 UTF-8 / GBK，但编辑保存仍统一写 UTF-8；暂未记录并回写每个值原有的非 UTF-8 编码。

## 五、安全与隐私核对

- 密码使用 WPF `PasswordBox` 掩码显示；编辑既有连接时 Password 字段不回显明文。
- 连接密码/SSH 密码经 DPAPI 加密写入 `%AppData%\WinRedis`；日志与错误文本做脱敏。
- 配置导出与“复制连接配置”均显式排除密码，并标记 `passwordIncluded=false`。
- 全文未使用 `KEYS *`；仅 `SCAN`（BigKey 同样 SCAN + MEMORY USAGE）。
- 危险命令在服务层分类与拦截；`FLUSHDB/FLUSHALL/CONFIG SET/SHUTDOWN` 等需二次授权，且高风险确认需输入命令名。
- 未向任何外部服务发送 Redis 数据；历史仅存本机，可关闭、单条删除、全部清空。
- 删除/重命名/TTL 操作后刷新列表并回读真实状态，不伪造成功提示。

## 六、测试与构建

```
dotnet build WinRedisClient.slnx -c Debug
→ 0 警告，0 错误

dotnet test WinRedisClient.slnx -c Debug
→ Core.Tests          93 passed
→ Redis.Tests          6 passed
→ PerformanceTests     3 passed
→ IntegrationTests    11 passed
→ 合计 132 passed / 0 failed
```

新增测试：`ConnectionConfigCopyTests` 校验复制配置不含 Redis 密码、SSH 密码与私钥口令；`KeyTreeBuilderTests` 校验目录树文件夹优先排序、默认折叠与展开状态恢复；`TextValueDecoderTests` 覆盖 UTF-8、GBK 回退与二进制转义；`ConnectionDirectoryTests` 覆盖连接分组目录构建、折叠/展开及持久化恢复、搜索忽略折叠、空分组移除与“已有分组 + 预置分组”的快速选择列表；`LocalizationTests` 校验中文直出、英文翻译、缺失键回退以及 en.json 的条目数量与占位符一致性。

## 七、运行方式

```powershell
dotnet run --project src/WinRedisClient.App/WinRedisClient.App.csproj
```

默认读取 `%AppData%\WinRedis` 下的设置、连接与历史。连接本机 Redis 后选择 DB9 即可看到本次验证使用的演示 Key（演示数据为本机手工准备，不随仓库分发）。

## 八、品牌资源

- 矢量源文件：[assets/winredis-logo.svg](../assets/winredis-logo.svg)（640×200，图标 + wordmark）
- 应用图标：[winredis-icon.ico](../src/WinRedisClient.App/Assets/winredis-icon.ico)（16/20/24/32/40/48/64/128/256 九种尺寸）与 [PNG](../src/WinRedisClient.App/Assets/winredis-icon.png)
- 图标同时用于 exe（`ApplicationIcon`）、窗口（`Window.Icon`）、任务栏、标题栏和设置页“关于”卡片；标题栏与关于卡片使用 “Win” + 紫色 “Redis” 的 wordmark，并随明暗主题切换文字颜色。
- 重新生成：`powershell -ExecutionPolicy Bypass -File tools\generate-brand-assets.ps1`

## 九、主题配色

设置页使用带侧边栏/卡片/强调色预览的卡片选择器，共 8 套：

| 主题 | 侧边栏 | 主背景 | 卡片 | 强调色 |
| --- | --- | --- | --- | --- |
| 浅色 Light | `#FFFFFF` | `#F5F7FB` | `#FFFFFF` | `#625BFF` |
| 深色 Dark | `#121725` | `#0E1220` | `#171C2B` | `#8B85FF` |
| 跟随系统 System | 自动 | 自动 | 自动 | 自动 |
| 海盐 Salt | `#E9F1F1` | `#F4F9F9` | `#FBFDFD` | `#C0523F` |
| 奶糖 Sugar | `#F3EBDD` | `#FBF7EF` | `#FFFDF8` | `#9C6528` |
| 森林 Forest | `#E6EEE3` | `#F5F9F2` | `#FCFEFA` | `#3F7B52` |
| 繁花 Flower | `#F0E8F0` | `#FAF6FA` | `#FEFBFE` | `#7C5AA6` |
| 微风 Wind | `#DDE1E3` | `#F1F3F4` | `#FAFBFB` | `#5F7078` |

5 套新主题均为低饱和浅色方案，只替换表面色、文字/边框色与强调色；连接状态、危险操作和 Key 类型仍使用统一的语义色。表面对比度按 WCAG 思路校验，强调色上的白色文字对比度约 4.6–5.4:1。

## 十、运行截图

1. [数据浏览与 Hash 详情](screenshots/01-browser-hash.png)
2. [String 编辑器](screenshots/02-string-editor.png)
3. [List 编辑器](screenshots/03-list-editor.png)
4. [Set 编辑器](screenshots/04-set-editor.png)
5. [ZSet 编辑器](screenshots/05-zset-editor.png)
6. [Stream 查看器](screenshots/06-stream-editor.png)
7. [实例监控](screenshots/07-monitor.png)
8. [命令终端](screenshots/08-cli.png)
9. [Key 对比](screenshots/09-diff.png)
10. [BigKey 分析](screenshots/10-bigkey.png)
11. [最近访问](screenshots/11-history-recent.png)
12. [命令历史](screenshots/12-history-commands.png)
13. [设置](screenshots/13-settings.png)
14. [深色主题](screenshots/14-dark-mode.png)
15. [全局命令面板](screenshots/15-command-palette.png)
16. [命令面板搜索](screenshots/16-palette-search.png)
17. [连接编辑与测试连接](screenshots/17-connection-dialog.png)
18. [新建 Key](screenshots/18-new-key-dialog.png)
19. [修改 TTL](screenshots/19-ttl-dialog.png)
20. [删除 Key 确认](screenshots/20-delete-confirm.png)
21. [FLUSHDB 高危确认](screenshots/21-danger-confirm.png)
22. [未保存切换提示](screenshots/22-unsaved-prompt.png)
23. [最大化后任务栏可见](screenshots/23-maximized-taskbar.png)
24. [全匹配搜索](screenshots/24-exact-search.png)
25. [自动批量加载](screenshots/26-batch-loading.png)
26. [连接默认批量加载选项](screenshots/27-connection-batch-option.png)
27. [默认本地模糊过滤](screenshots/28-local-search.png)
28. [数据库搜索性能确认](screenshots/29-db-search-confirm.png)
29. [确认后的数据库模糊搜索](screenshots/30-db-search-enabled.png)
30. [SSH 私钥认证表单](screenshots/31-ssh-key-auth.png)
31. [目录树文件夹优先](screenshots/32-folder-first.png)
32. [文件夹默认折叠](screenshots/33-folders-collapsed.png)
33. [JSON 默认格式化](screenshots/34-json-autoformat.png)
34. [文件夹展开状态记忆](screenshots/35-tree-state-memory.png)
35. [详情头部快捷操作](screenshots/36-detail-actions.png)
36. [BigKey 右侧预览](screenshots/37-bigkey-preview.png)
37. [品牌 Logo（SVG 渲染）](screenshots/38-brand-logo.png)
38. [标题栏、侧栏图标与设置页品牌（浅色）](screenshots/39-brand-light.png)
39. [标题栏、侧栏图标与设置页品牌（深色）](screenshots/40-brand-dark.png)
40. [图标多尺寸与任务栏效果](screenshots/41-brand-icon-sizes.png)
41. [界面字体大小设置](screenshots/42-font-size-setting.png)
42. [大字体（115%）效果](screenshots/43-font-size-large.png)
43. [海盐主题](screenshots/44-theme-salt.png)
44. [奶糖主题](screenshots/45-theme-sugar.png)
45. [森林主题](screenshots/46-theme-forest.png)
46. [繁花主题](screenshots/47-theme-flower.png)
47. [微风主题](screenshots/48-theme-wind.png)
48. [数据浏览改版（浅色，1340 宽）](screenshots/49-browser-redesign.png)
49. [数据浏览改版（1080 宽，筛选栏换行）](screenshots/50-browser-narrow.png)
50. [数据库下拉显示各库 Key 数量](screenshots/51-db-counts.png)
51. [超长 String 自动换行](screenshots/52-long-string-wrap.png)
52. [关闭自动换行后的横向滚动条](screenshots/53-long-string-scroll.png)
53. [连接列表分组与警告色](screenshots/54-connection-groups.png)
54. [连接分组与警告色设置](screenshots/55-connection-group-dialog.png)
55. [右键快速设置/移出分组](screenshots/56-connection-quick-group.png)
56. [连接分组目录树（折叠箭头 + 组内计数）](screenshots/57-connection-directory-tree.png)
57. [分组折叠状态（“我的”组收起）](screenshots/58-connection-group-collapsed.png)
58. [连接编辑对话框分组快速选择（已有 + 预置分组）](screenshots/59-group-quick-pick.png)
59. [右键菜单快速移动分组（已有/预置/新建/移出）](screenshots/60-group-context-menu.png)
60. [英文界面 · 数据浏览](screenshots/61-english-browser.png)
61. [英文界面 · 设置（语言切换入口）](screenshots/62-english-settings.png)
62. [英文界面 · 实例监控](screenshots/63-english-monitor.png)
63. [英文界面 · 命令终端](screenshots/64-english-command-console.png)
64. [英文界面 · Key 对比](screenshots/65-english-key-compare.png)
65. [中文界面 · 界面语言设置行](screenshots/66-language-setting-zh.png)
66. [关闭窗口选择（最小化到托盘 / 直接退出）](screenshots/67-close-choice.png)

> 批量/搜索测试使用临时 DB14 与 2,600 个 `qa:bulk:*` Key，验证完成后已按 Key 名清理，未使用 FLUSHDB。

## 十一、开源协议与安装包

- **许可证**：MIT（`LICENSE`）。选择理由：桌面客户端采用宽松许可便于个人与企业使用，且与全部依赖（StackExchange.Redis、SSH.NET、CommunityToolkit.Mvvm、Microsoft.Extensions.Logging，均为 MIT）兼容；如需专利明示可改用 Apache-2.0。
- **README**：`README.md`（简体中文）与 `README.en.md`（English），包含功能、安全设计、构建 / 测试 / 发布命令、目录结构、本地化说明与第三方组件许可。
- **安装包**：`tools/installer/WinRedis.iss`（Inno Setup 6）从 `publish/win-x64/WinRedisClient.exe` 生成按用户安装的 Setup.exe（免管理员权限、保留 `%AppData%\WinRedis` 用户数据）；流程与备选方案见 `docs/安装包制作.md`。
- **版本信息**：程序集版本 1.0.0，exe 文件属性包含 Product / Company / Copyright / Description。
