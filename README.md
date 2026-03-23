# RemoteRDPTool

<p align="center">
  <img src="./Assets/logo.jpg" alt="RemoteRDPTool Logo" width="140" />
</p>

面向 Windows 场景的远程连接与共享盘快捷工具，基于 Avalonia + .NET 8。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Avalonia](https://img.shields.io/badge/Avalonia-11.3.12-6A5ACD?logo=avalonia&logoColor=white)
![C%23](https://img.shields.io/badge/C%23-12.0-239120?logo=c-sharp&logoColor=white)
![MVVM](https://img.shields.io/badge/CommunityToolkit.Mvvm-8.2.1-0A66C2?logo=nuget&logoColor=white)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)

## 1. ✨ 核心能力

- 🗂️ 分组管理 RDP 连接（新增、编辑、删除、重命名分组）
- 🖥️ 一键发起远程桌面连接（`mstsc.exe`）
- 📁 一键打开共享盘（`net use` + `explorer.exe`）
- 📶 连接卡片实时 Ping 状态检测，支持自动降频策略
- ⏳ 全局加载遮罩：区分“正在远程连接 / 正在打开共享盘”
- 🛑 加载支持超时自动失败（默认 12 秒）与手动取消
- 🔁 处理 SMB 1219 多凭据冲突：按需清理后重试

---

## 2. 🧩 技术栈

- .NET 8（`net8.0`）
- Avalonia UI `11.3.12`
- CommunityToolkit.Mvvm `8.2.1`
- 平台：Windows（依赖 `mstsc.exe`、`cmdkey.exe`、`net.exe`、`explorer.exe`）

---

## 3. 📂 项目结构

```text
RemoteRDPTool/
├─ Models/
│  └─ AppConfig.cs                 # 配置模型（groups/connections/settings）
├─ Services/
│  ├─ AppConfigStore.cs            # 配置加载/保存与数据校验
│  ├─ RdpLauncher.cs               # RDP/共享盘启动与认证逻辑
│  └─ WindowService.cs             # 弹窗交互服务
├─ ViewModels/
│  └─ MainWindowViewModel.cs       # 主业务逻辑（命令、Ping、loading、取消）
├─ Views/
│  ├─ MainWindow.axaml             # 主界面
│  ├─ ConnectionEditWindow.axaml   # 连接编辑
│  ├─ CredentialPromptWindow.axaml # 凭据输入
│  └─ TextPromptWindow.axaml       # 文本输入
├─ dist/green/win-x64/             # 打包输出示例目录
├─ publish-green.bat               # 一键发布脚本
└─ RemoteRDPTool.csproj
```

---

## 4. 🚀 运行与构建

### 4.1 本地运行

```bash
dotnet restore
dotnet build
dotnet run --project .\RemoteRDPTool.csproj
```

### 4.2 发布（单文件）

```bash
.\publish-green.bat
```

发布脚本会输出到：

```text
dist\green\win-x64\
```

---

## 5. ⚙️ 配置文件说明（rdp-config.json）

程序默认读取可执行文件同目录下的 `rdp-config.json`。  
首次运行若无文件会自动生成初始配置。

### 5.1 字段结构

- `groups`：分组数组
  - `name`：分组名称
  - `connections`：连接数组
    - `id`：GUID（建议保留，缺失会自动补）
    - `name`：连接名称
    - `host`：目标主机/IP
    - `username`：账号
    - `password`：密码（明文，建议谨慎）
    - `shareDisk`：共享盘路径片段（如 `d$`）或完整 UNC 路径
- `settings`：应用设置
  - `autoReducePingFrequency`：是否自动降频
  - `pingIntervalSeconds`：常规 Ping 间隔（最小 2）
  - `reducedPingIntervalSeconds`：降频后间隔（不小于常规间隔）
  - `summonHotkey`：唤出按键（如 `Ctrl+R`）

### 5.2 配置示例

```json
{
  "groups": [
    {
      "name": "默认",
      "connections": [
        {
          "id": "00000000-0000-0000-0000-000000000001",
          "name": "生产前段",
          "host": "10.80.50.252",
          "username": "Administrator",
          "password": "YOUR_PASSWORD",
          "shareDisk": "d$"
        }
      ]
    }
  ],
  "settings": {
    "autoReducePingFrequency": true,
    "pingIntervalSeconds": 5,
    "reducedPingIntervalSeconds": 8,
    "summonHotkey": "Ctrl+R"
  }
}
```

---

## 6. 🔄 启动与配置迁移策略

应用启动时会优先使用 `AppContext.BaseDirectory\rdp-config.json`。  
若该文件不存在，或连接为空但 `%AppData%\RemoteRDPTool\rdp-config.json` 有连接，则自动复制迁移。  
也兼容从旧的当前工作目录配置迁移。

---

## 7. 🔐 共享盘连接策略（重点）

### 7.1 路径构造

- `shareDisk` 以 `\\` 开头：视为完整 UNC 路径直接使用
- 否则拼接为 `\\{host}\{shareDisk}`

### 7.2 凭据认证

- 使用 `net use` 建立认证会话
- 捕获标准输出/错误并带退出码返回

### 7.3 1219 冲突处理

当检测到 `1219`（多重凭据冲突）时：

1. 清理当前目标主机已有会话并重试
2. 仍冲突则执行一次全局清理并再次重试
3. 仍失败则抛出详细错误（路径、账号、退出码、输出、错误）

---

## 8. ⏱️ 加载、超时与取消机制

- 连接和共享盘都走统一全局 loading
- 默认超时：`12` 秒（`MainWindowViewModel` 常量）
- 手动点击“取消”会触发 `CancellationToken`
- 取消后：
  - 当前操作立即中断
  - 若本次已建立共享盘认证，会尝试 `net use <path> /delete /y` 回滚
  - UI 关闭 loading（取消不弹二次提示）

---

## 9. 🛡️ 安全建议

- 当前配置文件中的 `password` 为明文，建议：
  - 仅在受控内网/受控主机使用
  - 限制配置文件访问权限
  - 不要将真实配置提交到版本库
- 优先使用最小权限账号，不建议长期使用高权限账号访问共享

---

## 10. ❓常见问题

### Q1：共享盘提示 1219

这是 Windows SMB 多凭据冲突。工具已内置“按需清理后重试”逻辑。  
若仍失败，请检查是否有第三方进程占用同主机会话。

### Q2：点击连接后短暂无响应

已加入全局 loading，并支持超时与取消。  
若网络慢，可适当调大超时常量后重建。

### Q3：为什么有时不需要再次输入共享盘密码

Windows 会复用现有 SMB 会话，若凭据已缓存或已有有效连接，可能直接访问成功。

---

## 11. 🧪 开发建议

- 变更连接/共享盘流程后，至少执行：

```bash
dotnet build .\RemoteRDPTool.csproj -nologo
```

- 建议重点回归：
  - RDP 正常连接
  - 共享盘认证成功/失败分支
  - 1219 分支清理与重试
  - loading 超时与取消行为
