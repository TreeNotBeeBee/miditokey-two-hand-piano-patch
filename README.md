# MIDIToKey 2.8 双排双手钢琴补丁（非官方）

[![Build two-hand piano patcher](https://github.com/TreeNotBeeBee/miditokey-two-hand-piano-patch/actions/workflows/build.yml/badge.svg)](https://github.com/TreeNotBeeBee/miditokey-two-hand-piano-patch/actions/workflows/build.yml)

这是一个面向 Steam 版 **MIDIToKey 2.8 / build 22047399** 的独立 QWERTY 钢琴布局补丁。它把电脑键盘的上下两排变成两个可同时演奏、可分别移动八度的完整半音阶，适合左手伴奏、右手旋律。

本项目与原来的[单排 12 半音音区补丁](https://github.com/TreeNotBeeBee/miditokey-keyboard-layer-patch)是两个独立版本；本仓库不会继续修改或替代旧仓库的设计。

仓库**不包含 MIDIToKey 原版或修改版 EXE**。补丁器只读取使用者自己合法安装的官方程序，在本机注入本仓库公开、可审查的键盘逻辑。

## 键位

| 声部 | 白键 | 黑键 | 默认音区 | 移动八度 |
|---|---|---|---|---|
| 左手/上排 | `Q W E R T Y U` | `2 3 5 6 7` | C3–B3 | 左 Shift 降，右 Shift 升 |
| 右手/下排 | `Z X C V B N M` | `S D G H J` | C4–B4 | 左 Ctrl 降，右 Ctrl 升 |

白键依次是 `C D E F G A B`，黑键依次是 `C# D# F# G# A#`。完整图示与 MIDI 音高见 [键位表](docs/KEYMAP.md)。

## 主要特性

- 上下两排可以同时按下，适合双手和弦、低音与旋律。
- 两排音区完全独立，均可在 C1–B7 之间逐个八度移动。
- Shift/Ctrl 采用“单独点按并松开”换八度，不需要一直按住。
- 单独点按空格：两排同时还原到上排 C3、下排 C4。
- `Ctrl+A/C/V/Z/S`、`Shift+字母`、`Alt+Tab` 等组合仍传给 Windows，也不会误触音符或换八度。
- 忽略系统键盘自动连发：长按换八度键只执行一次。
- 切换音区时，已经按住的音会保持原音高直到物理键松开，避免卡音和中途变调。
- 窗口标题持续显示当前左右手音区。
- 键位由补丁直接提供，不需要在 MIDIToKey 中逐个录入映射。

## 安装

### 前置条件

1. 已通过 Steam 合法安装 MIDIToKey 2.8（build 22047399）。
2. Windows 10 或 Windows 11。
3. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更新版本。
4. 当前 `SMIDIToKey.exe` 必须是官方原版，不能叠加安装在另一个补丁上。

### 操作步骤

1. 点击 GitHub 页面右上角 **Code → Download ZIP**，然后完整解压。
2. 完全退出 MIDIToKey；任务管理器中不应再有 `SMIDIToKey.exe`。
3. 如果当前安装的是另一个补丁，先使用那个项目的恢复脚本，或在 Steam 中“验证已安装文件”。
4. 双击本仓库根目录的 `Apply-Patch.cmd`。
5. 第一次运行会下载开源依赖并编译补丁器，可能需要一两分钟。
6. 看到绿色的 `installed successfully` 后启动 MIDIToKey。

安装脚本会自动搜索常见 Steam 库，校验官方 EXE 的 SHA-256，并在覆盖前建立带时间戳的备份。遇到未知版本会拒绝修改。

当前支持的官方 EXE SHA-256：

```text
A23F92819F4C8EC6A42115F355C942259C081312A25E47DDDE97B3D6B1C82EE9
```

## 使用方法

1. 在 MIDIToKey 设置中开启“电脑键盘 → MIDI”和声音输出。
2. 需要在其它窗口中弹奏时，开启全局键盘监听。
3. 上排默认从 C3 开始，下排默认从 C4 开始。
4. 单独轻点左/右 Ctrl 调整下排，单独轻点左/右 Shift 调整上排。
5. 单独轻点空格，可以把两排一起恢复到默认中央音区。
6. 标题中的 `上排/左手 C3-B3 | 下排/右手 C4-B4` 会随切换更新。

修饰键与其它键一起使用时会被识别成普通系统操作。例如按住 Ctrl 再按 A 会执行全选；这次按键既不发音，也不移动八度。

空格同样只在单独点按时还原音区，`Ctrl+Space` 等组合不会触发还原。开启全局监听后，在其它软件中正常输入空格也会顺便恢复默认音区。

## 两个补丁版本如何切换

两个仓库可以同时保存在电脑中，但同一个 `SMIDIToKey.exe` 同一时间只能安装一个版本：

1. 退出 MIDIToKey。
2. 运行当前版本的 `Restore-Backup.cmd`，或者使用 Steam 验证文件恢复官方 EXE。
3. 进入另一个版本的文件夹，运行它的 `Apply-Patch.cmd`。

Steam 更新、验证文件或重装会覆盖补丁。只要版本仍受支持，重新运行对应仓库的安装脚本即可。

## 使用限制与安全提醒

- 普通办公键盘可能存在按键冲突（ghosting），部分三音或四音组合无法同时识别；支持 6-key rollover/N-key rollover 的键盘更适合演奏。
- 全局监听开启时，钢琴键仍会输入到当前软件。例如在聊天框弹奏也会打出字母。
- 如果出现字符无限连发、按键无法抬起或系统明显卡顿，请立即退出 MIDIToKey，不要继续测试。
- 本补丁不会模拟按键，也不会主动阻止 Windows 输入。
- 进入 Cubase 等 DAW 前建议退出 MIDIToKey，再使用 DAW 自带的电脑键盘录制功能，避免两个音源同时响应。

## 撤销

- 双击 `Restore-Backup.cmd` 可恢复本项目安装脚本创建的最近一次备份。
- 也可以在 Steam 中使用“验证已安装文件”恢复官方版本。
- 恢复脚本会先保留当前文件的安全副本，不会直接丢弃它。

## 从源码构建和验证

```powershell
dotnet build src/PatchPayload/PatchPayload.csproj -c Release
dotnet build src/Patcher/Patcher.csproj -c Release
```

补丁器使用 [dnlib](https://github.com/0xd4d/dnlib) 修改 .NET 程序集。`PatchPayload` 只包含本项目自行编写的替换逻辑和编译期类型外形，不包含 MIDIToKey 的实现代码。

本地静态验证不会启动 MIDIToKey、安装全局钩子或模拟任何键盘输入：

```powershell
.\tests\Verify-Patch.ps1 -OfficialExe 'C:\path\to\official\SMIDIToKey.exe'
```

## 免责声明与许可

本项目与 MIDIToKey、srammark、Valve 或 Steinberg 没有隶属或授权关系。MIDIToKey 的名称和程序版权属于其各自权利人；使用者必须自行拥有合法副本。

本仓库自行编写的补丁器、载荷源码和脚本采用 [MIT License](LICENSE)。该许可不适用于 MIDIToKey 本身，也不授予分发 MIDIToKey EXE 的权利。

---

English summary: unofficial, source-only two-hand QWERTY piano patcher for a legally installed Steam copy of MIDIToKey 2.8. Upper and lower rows form independent chromatic octaves. No original or modified MIDIToKey executable is included.
