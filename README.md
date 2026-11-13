# Guitar_Bass_Tuner

弦准是一款 Windows 桌面吉他和贝斯调音器，支持六弦吉他、四弦贝斯、Rocksmith USB Guitar Adapter 和普通麦克风。

## 使用方法

1. 连接 Rocksmith 输入线或麦克风，在「音频输入」中选择设备，也可以使用 Windows 默认输入。
2. 选择乐器和定调。预设包括标准调弦、全弦降半音、全弦降全音、Drop D 和 Drop C。
3. 单独拨一根空弦。自动模式会选择音高最接近的弦，也可以点击琴颈或音名来锁定弦位。

界面同时显示当前音、目标音、频率和音分偏差。指针和最近 8 秒的曲线共用一条目标线，偏差在 ±3 音分内时显示「已调准」。瞬时跳音过滤会在短暂的音高跳变期间保留稳定读数。

## 从源码运行

需要 Windows 和 .NET 10 SDK。在项目目录执行：

```powershell
dotnet run --project .\GuitarBassTuner.csproj -c Release
```

在 Windows x64 上生成单文件程序：

```powershell
dotnet publish .\GuitarBassTuner.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\release
```

生成的 `release\GuitarBassTuner.exe` 使用 .NET 10 Desktop Runtime。音频通过 Windows WASAPI 读取，调音分析在本机进行。
