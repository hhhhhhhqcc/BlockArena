# BlockArena 原创音频

《Luminous Clearing》由 `Generate-ArenaAudio.py` 使用 Python 标准库程序合成，旋律、和声、音色和音效均为本项目原创；不包含第三方录音、采样、音色库或下载素材。

菜单、准备、战斗三首循环曲共享 96 BPM、16 小节、40 秒的主题结构。菜单采用柔和和声与稀疏玻璃琴音；准备阶段增加轻拨弦和低音；战斗加入轻鼓点、紧凑低音和琶音。它们使用相同时间基准，阶段切换时保持拍点，并用 0.85 秒交叉淡化。

资源为 24 kHz、16 bit、双声道 PCM WAV，共 3 首音乐和 20 个音效，约 12.42 MiB。音乐峰值不超过 0.52，目标 RMS 约 0.105–0.115；音效峰值不超过 0.48，均预留混音余量。循环尾音写回下一段开头，避免裁切。`Assets/Resources/ArenaAudio/manifest.json` 保存每个成品 WAV 的实际峰值、RMS、循环边界差值、削波样本数和 SHA-256。

## 接入

`ArenaAudio.Initialize()` 创建持久控制器；无图形设备、batchmode 和 `-arena-server` 跳过创建与资源加载。默认音乐音量 0.4，音效 0.65。`SetVolumes(music, sfx, muted = false)` 立即应用并延迟保存 PlayerPrefs。两参数调用会解除显式静音，便于音量滑杆恢复。

`SetPhase` 将 `battle` 对应战斗音乐，`prep`、`recruit`、`results` 对应准备音乐，其余对应菜单音乐。音乐静音时保留播放时间，恢复音量时继续当前拍点。

`Play(cue, world, intensity = 1)` 使用十个音效声道，其中两个优先留给仪式、玩家受击与胜负。普通战斗按类别限频约 0.1–0.18 秒并降低音量。所有音效均按 2D 播放，世界位置通过摄像机视口映射出轻微左右声像，避免俯视相机的距离衰减。音高微变使用独立确定性计数，不消耗玩法随机数。

主 cue：`click`、`purchase`、`coin`、`pickup`、`place`、`recruit`、`battle_start`、`attack`、`ritual_rise`、`ritual_charge`、`ritual_launch`、`commander_hit`、`victory`、`defeat`、`combat_arrow`、`combat_hit`、`combat_explode`、`combat_death`、`combat_heal`、`error`。

别名：`ui_click`、`buy`、`gold`、`drag`、`drop`、`start`、`win`、`lose`、`open`、`close`、`ready`、`refresh`、`choose`、`skip`、`ui_open`、`ui_close`、`battle`。

验收可读取 `ClipsLoaded`（23）、`MusicTrackCount`（3）、`CurrentMusic`、`MusicIsPlaying` 和 `SoundCount`；后者只在实际分配声道并调用播放后递增。原生可视播放器还可通过 `AudioListener.GetOutputData` 检查混音输出。

## 重新生成

运行 `python -B Tools/Generate-ArenaAudio.py`，默认写入当前脚本所属工程的 `Assets/Resources/ArenaAudio`。可用 `--output <目录>` 指定路径。固定种子保证相同版本输出一致；已有 `.meta` 的 GUID 会保留。无需安装 Python 扩展。
