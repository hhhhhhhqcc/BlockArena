# BlockArena 原创音频

## v0.9.3 当前音频

当前资源总数仍为 **40 项：3 首音乐、37 个音效**。本版替换既有 `click.wav` 与 `ui_hover.wav` 的原创程序合成内容，保留资源路径和 GUID，不新增采样、音色库或音频文件。`click` 为约 **0.105 秒**的短木石敲击声，`ui_hover` 缩短至约 **0.06 秒**，用于轻量进入反馈；生成来源分别为 `Tools/Generate-ArenaAudio.py` 和 `Tools/Generate-InterfaceAudio.py`。

按钮激活统一调用 `PlayUiClick()` 或 `ui_click`：`click`、`ui_toggle`、`ready`、`ui_open`、`ui_close`、`open`、`close`、`skip` 共用 `click` 定义，固定音高为 **1**、基础增益为 **0.48**。旧调用点传入不同的正强度也保持同音量，实际输出再乘玩家音效音量；强度为 0、静音或音效音量为 0 时不播放。别名共用 **0.035 秒**限频，避免一个操作在同帧触发两次相同点击声。鼠标按钮和键盘开关沿用同一入口。

HUD 只负责操作点击，战术面板的全选、节点修改、保存、就绪和瞄准不再额外叠加选中、布点或放置声；地图上的选中、拖放和右键布点保留对应反馈。轻量悬停仍按进入和限频处理，受音效音量控制。技能、设置面板遮挡仅影响输入，不通过禁用下层按钮改变其外观。

购买与换技的结果音由服务器快照驱动：接受新生物或永久技能购买后播放 `purchase`，接受指挥技变更后播放 `skill_select`。点击购买时先有统一点击反馈，尚未确认不会提前播放购买成功音；拒绝、仅刷新商品、遣散、初始生物分配、重复快照、首次连接及跨轮同步不会误报购买或换技成功。金币增加仍保留独立到账提示。

`ArenaPresentationProbe` 覆盖按钮别名、旧调用强度、固定音高、同帧去重、静音、音量总线和确认基线，并保留水晶演出与音频回归。整版复验入口为 `Tools/Verify-InterfaceUpdate.ps1 -ShowWindows`，日志为 `.checks/*-0.9.3.log`，截图位于 `Captures/0.9.3`；本说明描述验证范围，实际运行结果以 [验证记录](../验证记录.md) 为准。

完整重新生成仍依次使用下方三份脚本；基础 27 项、地图生成 5 项和交互 8 项资源的划分保持不变，`ArenaAudio.ClipsLoaded` 预期为 **40**、`MusicTrackCount` 预期为 **3**，专用服务器继续不创建音频。

## v0.9.2 接入记录

v0.9.2 客户端加载 **40 项原创音频资源：3 首音乐、37 个音效**，在既有 32 项资源上新增下列八个程序合成音效，不使用外部采样；实际加载、音频输出与回归结果统一见 [验证记录](../验证记录.md)。下方早期记录中的数量仅对应当时版本。

| cue | 用途 |
| --- | --- |
| `ui_hover` | 轻量悬停反馈，按进入和限频处理 |
| `ui_toggle` | 设置、开合面板及准备按钮反馈 |
| `route_node` | 新增路线节点或等待节点 |
| `skill_select` | 选择本轮 Steve 指挥技 |
| `draw_tick` | 地图、天气轮盘的抽取滴答 |
| `map_draw` | 地图抽取开始 |
| `weather_draw` | 天气抽取开始 |
| `tactics_start` | 进入战术编排 |

新增资源由 `Tools/Generate-InterfaceAudio.py` 使用固定种子和 Python 标准库生成，格式为 **24 kHz、16 bit、单声道 PCM WAV**。`Assets/Resources/ArenaAudio/interface-manifest.json` 记录文件、时长、峰值、RMS、削波样本数和 SHA-256；既有音乐及早期音效继续保留原格式。

完整重新生成时依次运行：

```powershell
python -B Tools/Generate-ArenaAudio.py
python -B Tools/Generate-BattlefieldAudio.py
python -B Tools/Generate-InterfaceAudio.py
```

三份脚本分别提供基础 27 项、地图生成 5 项和本次交互 8 项资源；总加载数以 `ArenaAudio.ClipsLoaded` 的 **40** 为当前预期，`MusicTrackCount` 仍为 **3**。专用服务器继续跳过音频创建与加载。

地图与天气轮盘通过 `ArenaRevealAudio.DrawIndex(stage, age, choices)` 共用由快到慢的步进。音频读取权威揭晓阶段，按阶段和轮次去重；首次接入或重连建立静默基线，重复快照不会重播开场，延迟最多播放当前一声滴答，不补播旧节拍。倒计时也沿用去重和重连基线，避免重连后连响。

右上独立齿轮面板统一音乐、音效与静音；交互 cue 走音效音量，不改变玩法随机数。Steve 与生物范围开关也位于该面板，显示偏好保存在本机。整版复验入口为 `Tools/Verify-PolishUpdate.ps1 -ShowWindows`，当前验收结果以验证记录为准。

## 早期接入记录

《Luminous Clearing》由 `Generate-ArenaAudio.py` 使用 Python 标准库程序合成，旋律、和声、音色和音效均为本项目原创；不包含第三方录音、采样、音色库或下载素材。

菜单、准备、战斗三首循环曲共享 96 BPM、16 小节、40 秒的主题结构。菜单采用柔和和声与稀疏玻璃琴音；准备阶段增加轻拨弦和低音；战斗加入轻鼓点、紧凑低音和琶音。它们使用相同时间基准，阶段切换时保持拍点，并用 0.85 秒交叉淡化。

资源为 24 kHz、16 bit、双声道 PCM WAV，共 3 首音乐和 24 个音效，约 12.67 MiB。音乐峰值不超过 0.52，目标 RMS 约 0.105–0.115；音效峰值不超过 0.48，均预留混音余量。循环尾音写回下一段开头，避免裁切。`Assets/Resources/ArenaAudio/manifest.json` 保存每个成品 WAV 的实际峰值、RMS、循环边界差值、削波样本数和 SHA-256。

## 接入

`ArenaAudio.Initialize()` 创建持久控制器；无图形设备、batchmode 和 `-arena-server` 跳过创建与资源加载。默认音乐音量 0.4，音效 0.65。`SetVolumes(music, sfx, muted = false)` 立即应用并延迟保存 PlayerPrefs。两参数调用会解除显式静音，便于音量滑杆恢复。

`SetPhase` 将 `battle` 对应战斗音乐，`prep`、`recruit`、`results` 对应准备音乐，其余对应菜单音乐。音乐静音时保留播放时间，恢复音量时继续当前拍点。

`Play(cue, world, intensity = 1)` 使用十个音效声道，其中两个优先留给仪式、玩家受击与胜负。普通战斗按类别限频约 0.1–0.18 秒并降低音量。所有音效均按 2D 播放，世界位置通过摄像机视口映射出轻微左右声像，避免俯视相机的距离衰减。音高微变使用独立确定性计数，不消耗玩法随机数。

主 cue：`click`、`purchase`、`coin`、`pickup`、`place`、`recruit`、`battle_start`、`attack`、`ritual_rise`、`ritual_charge`、`ritual_launch`、`commander_hit`、`victory`、`defeat`、`combat_arrow`、`combat_hit`、`combat_explode`、`combat_death`、`combat_heal`、`error`。

别名：`ui_click`、`buy`、`gold`、`drag`、`drop`、`start`、`win`、`lose`、`open`、`close`、`ready`、`refresh`、`choose`、`skip`、`ui_open`、`ui_close`、`battle`。

验收可读取 `ClipsLoaded`（23）、`MusicTrackCount`（3）、`CurrentMusic`、`MusicIsPlaying` 和 `SoundCount`；后者只在实际分配声道并调用播放后递增。原生可视播放器还可通过 `AudioListener.GetOutputData` 检查混音输出。

## 重新生成

0.7.0 新增 `countdown`、`countdown_urgent`、`execution_charge`、`execution_hit`。倒计时按权威快照在招募、准备及战斗最后五秒提醒，最后三秒使用更急的双音；相同秒数不重复，延迟包不补播错过的秒数，重连先建立基线。提醒与处决使用关键音效声道，跟随音效音量与静音设置。

运行 `python -B Tools/Generate-ArenaAudio.py`，默认写入当前脚本所属工程的 `Assets/Resources/ArenaAudio`。可用 `--output <目录>` 指定路径。固定种子保证相同版本输出一致；已有 `.meta` 的 GUID 会保留。无需安装 Python 扩展。
