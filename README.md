# AccountManager

Windows 本地账号管理器，使用 C# WPF + SQLite。

## 功能

- 首次启动设置主密码、密码提示词、密码保护问题/答案
- 启动必须输入主密码解锁
- 忘记主密码时可通过保护问题重置主密码
- 账号字段：邮箱、密码、2FA、分类、标签、备注
- TXT / 粘贴批量导入：`邮箱--密码--2FA`
- 搜索大小写不敏感
- 双向部分匹配：数据库邮箱 `bqmlutxsw20435@outlook.jp` 可以被 `codex-bqmlutxsw20435@outlook.jp-plus` 命中
- 命中内容高亮：黄色表示搜索词在字段内，绿色表示字段整体被搜索词包含
- 导出 TXT / CSV
- 创建完整加密数据库备份，文件名：`AccountManager_Backup_yyyyMMdd_HHmmss.db`
- 导入备份后会重启，必须输入该备份对应的主密码

## 数据目录

同一套代码支持两个发布形态：

| 版本 | 数据位置 |
|---|---|
| 绿色便携版 | exe 同目录下的 `data/accounts.db` |
| 安装版 | `%APPDATA%/AccountManager/accounts.db` |

## 构建

开发构建：

```powershell
dotnet build
```

绿色便携版：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
```

输出：

```text
dist/portable/AccountManager.exe
dist/AccountManager_Portable.zip
```

安装版应用文件：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-installed.ps1
```

输出：

```text
dist/installed/app/AccountManager.exe
```

如需生成安装包，先安装 Inno Setup 6，然后重新运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-installed.ps1
```

输出：

```text
dist/installed/AccountManager_Setup.exe
```

## 备份说明

“创建备份”会复制完整 SQLite 数据库。账号密码、2FA、备注字段以本地加密形式存储，备份里也保留主密码配置、提示词和保护问题配置。

导入备份后程序会重启；恢复后的数据库需要输入该备份创建时对应的主密码才能使用。
