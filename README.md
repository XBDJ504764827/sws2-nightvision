# SWS2 Nightvision

玩家独立夜视插件，通过个人后处理曝光提高暗图可见度。支持 `!nv` 开关、`!nvi` 调整强度和个人偏好保存，效果只向拥有者传输，不修改整张地图的光照。

## 与其他 SWS2 插件搭配

| 插件 | 职责 | 与本插件的关系 |
| --- | --- | --- |
| [sws2-cs2menus](https://github.com/XBDJ504764827/sws2-cs2menus) | 共享菜单、分页、导航、地图选择 HUD | 可选；不依赖菜单服务 |
| [sws2-cs2mapmanager](https://github.com/XBDJ504764827/sws2-cs2mapmanager) | 统一地图目录、评分、工坊下载和可取消换图 | 可选；不依赖地图服务 |
| [sws2-cs2admin](https://github.com/XBDJ504764827/sws2-cs2admin) | 管理员权限、玩家管理、封禁、换图控制 | 可选；亮度权限使用 SwiftlyS2 权限检查 |
| [sws2-cs2rockthevote](https://github.com/XBDJ504764827/sws2-cs2rockthevote) | 玩家预定地图、RTV 发起投票与选图 | 可选；预定和投票独立工作 |
| [sws2-flashlight](https://github.com/XBDJ504764827/sws2-flashlight) | 随视角移动的手电筒 | 可选；局部照明与全局曝光可同时使用 |
| [sws2-nightvision](https://github.com/XBDJ504764827/sws2-nightvision) | 玩家独立夜视与亮度偏好 | 本插件 |

常用组合：玩家换图安装 **menus + mapmanager + rockthevote**；需要管理面板时再加 **admin**；手电筒和夜视按需加入。工坊地图自动下载和 HUD 工坊资源自动挂载还需要服务器安装 **MultiAddonManager**。本套件不提供 KZ 计时、检查点或 CKZ/VNL 模式本身。

## 安装与快速使用

仅依赖 [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2)，不需要菜单、管理员、地图管理、MAM 或客户端 HUD addon。

1. 下载本仓库发布包，完整解压 `sws2-nightvision/` 到 `csgo/addons/swiftlys2/plugins/`。
2. 重启服务器，或在服务器控制台执行 `sw plugins load sws2-nightvision`。
3. 玩家进入队伍并出生后输入 `!nv`；默认强度 `1.0`，暗图可尝试 `!nvi 2` 或 `!nvi 3`。
4. 用 `!nvstatus` 查看状态，`!nv off` 关闭。默认禁止观察者使用。

## 命令与按键绑定

聊天前缀取决于服务器设置，本文使用 `!`。命令也注册了无前缀玩家控制台形式。

| 命令 | 作用 |
| --- | --- |
| `!nv` / `!nightvision` | 切换开关 |
| `!nv on` / `!nv off` / `!nv toggle` | 明确启用、关闭或切换 |
| `!nvi` / `!nvintensity` | 查看当前强度和允许范围 |
| `!nvi <数值>` / `!nvintensity <数值>` | 设置个人强度，例如 `!nvi 2.5` |
| `!nvstatus` | 查看启用状态、效果是否活跃和强度 |
| `!nvdebug` | 把诊断写到玩家控制台；开启 `Debug` 时也写服务器日志 |

可在玩家控制台绑定一个未占用的键：

```text
bind "n" "nv"
```

强度为曝光值，数值越大画面越亮。默认 `1.0` 是温和提升，不是自动适配所有地图的保证；建议从 `2.0`–`3.0` 逐步测试，避免直接用最大值。

## 配置

首次加载生成 `csgo/addons/swiftlys2/configs/plugins/sws2-nightvision/config.jsonc`，支持热重载。

```jsonc
{
  "sws2-nightvision": {
    "Debug": false,
    "Behavior": {
      "Enabled": true,
      "DefaultIntensity": 1.0,
      "MinIntensity": 0.5,
      "MaxIntensity": 10.0,
      "AllowIntensityCommand": true,
      "IntensityPermission": "",
      "NotifyOnToggle": true,
      "TurnOffOnDeath": false,
      "TurnOffOnTeamSwitch": true,
      "DisableInSpectator": true,
      "SavePreferences": true,
      "RestorePreferencesOnSpawn": true
    },
    "Effect": {
      "FadeDuration": 0.0,
      "ExposureCompensation": 0.0
    }
  }
}
```

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `Debug` | `false` | 将额外诊断写入日志 |
| `Behavior.Enabled` | `true` | 夜视功能总开关 |
| `Behavior.DefaultIntensity` | `1.0` | 无已保存个人偏好时的默认强度 |
| `Behavior.MinIntensity` / `MaxIntensity` | `0.5` / `10.0` | 玩家可设置的强度范围 |
| `Behavior.AllowIntensityCommand` | `true` | 是否允许用 `!nvi` 修改强度 |
| `Behavior.IntensityPermission` | 空字符串 | 修改强度所需的 SwiftlyS2 权限；为空则所有玩家可用 |
| `Behavior.NotifyOnToggle` | `true` | 开关聊天提示 |
| `Behavior.TurnOffOnDeath` | `false` | 死亡时关闭并清除启用偏好 |
| `Behavior.TurnOffOnTeamSwitch` | `true` | 实际切换队伍时关闭并清除启用偏好 |
| `Behavior.DisableInSpectator` | `true` | 禁止观察者使用 |
| `Behavior.SavePreferences` | `true` | 保存偏好到 `preferences.json` |
| `Behavior.RestorePreferencesOnSpawn` | `true` | 出生时按启用偏好恢复 |
| `Effect.FadeDuration` | `0.0` | 效果淡入时间，秒 |
| `Effect.ExposureCompensation` | `0.0` | 在强度基础上的额外曝光补偿 |

`IntensityPermission` 检查 `Core.Permission`；仅向 admin 的自有 `admins.jsonc` 写入自定义节点，不等于自动授予此 SwiftlyS2 权限。若要限制亮度调整，应在 SwiftlyS2 权限配置授予相应节点；开关功能本身没有管理员权限要求。

升级不会覆盖现有配置，也不会把已有玩家强度自动改成新的 `DefaultIntensity`。修改默认值主要作用于没有个人偏好的玩家；已有玩家用 `!nvi` 自行调整。

## 偏好、死亡、换队和换图

偏好文件：`csgo/addons/swiftlys2/configs/plugins/sws2-nightvision/preferences.json`。包含玩家强度和启用偏好；`SavePreferences` 决定是否持久化。更新插件时保留此文件。

| 事件 | 当前行为 |
| --- | --- |
| 玩家死亡 | 默认不清除启用偏好；设 `TurnOffOnDeath: true` 则关闭并清除，出生不会据旧启用状态恢复 |
| 实际切换队伍 | 默认关闭并清除启用偏好；初次入队、断线事件和同队事件被过滤 |
| 加入观察者 | 默认不能使用；若关闭换队清除，但保留观察者限制，则暂停效果而保留偏好 |
| 正常地图卸载 | 清理实体并保留偏好，满足出生恢复条件时重新应用 |
| 玩家断线后重新加入 | 清除启用状态，下次需手动开；保存的强度可继续使用 |
| 服务器冷启动 | 清除已保存的启用标记，玩家从关闭状态开始；强度保留 |
| 插件热重载 | 尝试恢复当前在线玩家偏好和效果 |

因此，RTV/admin 换图本身不直接操作夜视；只要没有断线或清除偏好的队伍变化，出生恢复设置允许时可恢复。MAM 导致客户端重连时，按断线规则处理，需要重新开启。

## 与手电筒和管理员功能搭配

flashlight 的光束提供视线方向的局部照明，nightvision 调整整体画面曝光；二者可以同时用，默认仅影响拥有者。可以先设置 `!nvi 2`，再按 `F` 观察近处；泛白时降低强度或关闭其中一个。

admin 的第一人称观战会将管理员切换到观察者队伍，夜视将遵守上表的团队和观察者规则。menus 的 HUD、RTV 选图和地图评分不依赖夜视，安装夜视也不会自动给这些菜单添加按钮。

## 排查画面无变化、闪烁或过亮

1. 运行 `!nvstatus`，确认功能开启且效果处于活跃状态；检查是否为观察者、是否尚未出生。
2. 运行 `!nvdebug`，在玩家控制台查看诊断；将配置 `Debug` 设为 `true` 可同时记录服务器日志。
3. 确认客户端后处理未关闭，可在客户端控制台检查/设置 `mat_postprocess_enable 1`。
4. 先停用手电筒或其他后处理插件，再从较低强度测试；对比 `de_dust2` 等常规地图，判断是否为地图自身 master 后处理或 tonemap 冲突。
5. 检查服务器 `[sws2-nightvision]` 日志，确认实体创建和偏好恢复成功。

实现使用个人 `post_processing_volume` 的 `Master` / `ExposureControl`，并附加到拥有者相机的后处理列表；不删除或修改地图原有实体。关闭时将卷切到地图曝光范围（或中性 `1.0`）的空闲状态，减少移除重建的闪烁；断线/换图/卸载才移除。其他 master 后处理仍可能与其交互，需要在实际地图验证。

## 构建、升级与发布

源码构建需要 .NET 10 SDK：

```sh
dotnet restore sws2-nightvision.csproj
dotnet build sws2-nightvision.csproj -c Release
dotnet publish sws2-nightvision.csproj -c Release
```

产物为 `build/sws2-nightvision.zip`。本插件没有其他 SWS2 合约依赖，可独立更新；保留 `config.jsonc` 和 `preferences.json`，替换完整包并验证开关、强度、死亡/换图恢复行为。

仓库采用 `develop` → `main`，PR 构建测试包和预览发布，合并 main 自动计算版本并发布；`src/VersionInfo.cs` 为本地回退版本。

## 致谢与许可证

- [rcnoob/Nightvision](https://github.com/rcnoob/Nightvision)：CS2 曝光控制方案。
- [GAMMACASE/NightVision](https://github.com/GAMMACASE/NightVision)：原 CS:GO 夜视插件。
- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2)：框架与插件约定。
- [MIT License](LICENSE)。
